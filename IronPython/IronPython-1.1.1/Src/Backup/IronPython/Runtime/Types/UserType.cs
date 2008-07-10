/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public
 * License. A  copy of the license can be found in the License.html file at the
 * root of this distribution. If  you cannot locate the  Microsoft Public
 * License, please send an email to  dlr@microsoft.com. By using this source
 * code in any fashion, you are agreeing to be bound by the terms of the 
 * Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using IronPython.Runtime.Calls;
using IronPython.Runtime.Operations;
using IronPython.Compiler.Generation;
using IronPython.Modules;
using IronMath;

namespace IronPython.Runtime.Types {
    /// <summary>
    /// UserType represents the type of new-style Python classes (which can inherit from built-in types). 
    /// 
    /// Object instances of new-style Python classes are represented by classes generated by NewTypeMaker,
    /// and are named IronPython.NewTypes.someName.
    /// 
    /// OldClass is the equivalent of UserType for old-style Python classes (which cannot inherit from 
    /// built-in types).
    /// </summary>

    [Flags]
    enum UserTypeFlags {
        None = 0x0000,
        HasSlots = 0x0001,
        HasFinalizer = 0x0002,
        HasWeakRef = 0x0004,
        HasDictionary = 0x008,
    }

    [DebuggerDisplay("UserType: {ToString()}")]
    [PythonType(typeof(DynamicType))]
    public class UserType : DynamicType, IWeakReferenceable {
        // This is typed as "object" instead of "string" as the user is allowed to set it to an arbitrary object
        public object __module__;
        UserTypeFlags flags;

        #region Public API Surface

        public static UserType MakeClass(string name, Tuple bases, IDictionary<object, object> dict) {
            return new UserType(name, bases, dict);
        }

        /// <summary>
        /// called from generated code for overridden methods
        /// </summary>
        public bool TryGetNonInheritedMethodHelper(object instance, IAttributesDictionary instanceDict, SymbolId name, int key, out object value) {
            if (instanceDict != null) {
                if (instanceDict.TryGetValue(name, out value)) return true;
            }
            if (((NamespaceDictionary)dict).TryGetNonInheritedValue(key, out value)) {
                value = Ops.GetDescriptor(value, instance, this);
                return true;
            }
            return false;
        }

        public bool TryGetNonInheritedValueHelper(int key, out object value) {
            return ((NamespaceDictionary)dict).TryGetNonInheritedValue(key, out value);
        }


        public static object GetPropertyHelper(object prop, object instance, SymbolId name) {
            IDescriptor desc = prop as IDescriptor;
            if (desc == null) {
                throw Ops.TypeError("Expected property for {0}, but found {1}",
                    name.GetString(), Ops.GetDynamicType(prop).__name__);
            }
            return desc.GetAttribute(instance, null);
        }

        public static void SetPropertyHelper(object prop, object instance, object newValue, SymbolId name) {
            IDataDescriptor desc = prop as IDataDescriptor;
            if (desc == null) {
                throw Ops.TypeError("Expected settable property for {0}, but found {1}",
                    name.GetString(), Ops.GetDynamicType(prop).__name__);
            }
            desc.SetAttribute(instance, newValue);
        }

        public static void AddRemoveEventHelper(object method, object instance, UserType context, object eventValue, SymbolId name) {
            object callable = Ops.GetDescriptor(method, instance, context);
            if (!Ops.IsCallable(callable)) {
                throw Ops.TypeError("Expected callable value for {0}, but found {1}", name.GetString(),
                    Ops.GetDynamicType(method).Name);
            }

            Ops.Call(callable, eventValue);
        }

        #endregion

        #region Constructors

        protected UserType(string name, Tuple bases, IDictionary<object, object> dict)
            : base(NewTypeMaker.GetNewType(name, bases, dict)) {
            ctor = BuiltinFunction.MakeMethod(name, type.GetConstructors(), FunctionType.Function);

            ValidateSupportedInheritance(bases, dict);

            IAttributesDictionary fastDict = (IAttributesDictionary)dict;

            this.__name__ = name;
            this.__module__ = fastDict[SymbolTable.Module];   // should always be present...

            if (fastDict.ContainsKey(SymbolTable.Slots)) {
                HasSlots = true;
                if (dict.ContainsKey("__dict__")) HasDictionary = true;
                if (dict.ContainsKey("__weakref__")) HasWeakRef = true;
            } else {
                HasDictionary = true;
                HasWeakRef = true;
            }

            PopulateDefaultDictionary(fastDict);

            InitializeUserType(bases, fastDict, false);

            this.dict = CreateNamespaceDictionary(dict);

            AddProtocolWrappers();
        }

        private void PopulateDefaultDictionary(IAttributesDictionary fastDict) {
            if (!fastDict.ContainsKey(SymbolTable.Doc)) {
                fastDict[SymbolTable.Doc] = null;
            }

            if (HasWeakRef && !fastDict.ContainsKey(SymbolTable.WeakRef)) {
                fastDict[SymbolTable.WeakRef] = new WeakRefWrapper(this);
            }

            if (!fastDict.ContainsKey(SymbolTable.Dict)) {
                fastDict[SymbolTable.Dict] = new DictWrapper(this);
            }
        }

        private void ValidateSupportedInheritance(Tuple bases, IDictionary<object, object> dict) {
            if (type.GetInterface("ICustomAttributes") == typeof(ICustomAttributes)) {
                // ICustomAttributes is a well-known type. Ops.GetAttr etc first check for it, and dispatch to the
                // ICustomAttributes implementation. At the same time, built-in types like PythonModule, DynamicType, 
                // Super, SystemState, etc implement ICustomAttributes. If a user type inherits from these,
                // then Ops.GetAttr still dispatches to the ICustomAttributes implementation of the built-in types
                // instead of checking the user-type.
                if (dict.ContainsKey(SymbolTable.GetAttribute.ToString()))
                    throw new NotImplementedException("Overriding __getattribute__ of built-in types is not implemented");
                if (dict.ContainsKey(SymbolTable.SetAttr.ToString()))
                    throw new NotImplementedException("Overriding __setattr__ of built-in types is not implemented");
                if (dict.ContainsKey(SymbolTable.DelAttr.ToString()))
                    throw new NotImplementedException("Overriding __delattr__ of built-in types is not implemented");
            }

            // we don't support overriding __mro__
            if (dict.ContainsKey(SymbolTable.MethodResolutionOrder.ToString()))
                throw new NotImplementedException("Overriding __mro__ of built-in types is not implemented");

            // cannot override mro when inheriting from type
            if (dict.ContainsKey("mro")) {
                foreach (object o in bases) {
                    DynamicType dt = o as DynamicType;
                    if (dt != null && dt.IsSubclassOf(TypeCache.DynamicType)) {
                        throw new NotImplementedException("Overriding type.mro is not implemented");
                    }
                }
            }
        }

        /// <summary>
        /// Set up the type
        /// </summary>
        /// <param name="resetType">Is an existing type being reset?</param>
        void InitializeUserType(Tuple newBases, IAttributesDictionary newDict, bool resetType) {
            newBases = EnsureBaseType(newBases);

            for (int i = 0; i < newBases.Count; i++) {
                for (int j = 0; j < newBases.Count; j++) {
                    if (i != j && newBases[i] == newBases[j]) {
                        throw Ops.TypeError("duplicate base class {0}", ((IPythonType)newBases[i]).Name);
                    }
                }
            }

            if (resetType) {
                // Ensure that we are not switching the CLI type
                Type newType = NewTypeMaker.GetNewType(__name__.ToString(), newBases, (IDictionary<object, object>)dict);
                if (type != newType)
                    throw Ops.TypeErrorForIncompatibleObjectLayout("__bases__ assignment", this, newType);

                foreach (object baseTypeObj in BaseClasses) {
                    if (baseTypeObj is OldClass) continue;
                    DynamicType baseType = baseTypeObj as DynamicType;
                    baseType.RemoveSubclass(this);
                }
            }

            this.bases = newBases;

            // if our dict, or any of our children, have a finalizer, then
            // we have a finalizer.
            bool hasFinalizer = newDict.ContainsKey(SymbolTable.Unassign);

            foreach (object baseTypeObj in BaseClasses) {
                if (baseTypeObj is OldClass) continue;
                DynamicType baseType = baseTypeObj as DynamicType;
                baseType.AddSubclass(this);

                UserType ut = baseType as UserType;
                if (ut != null && ut.HasFinalizer) {
                    hasFinalizer = true;
                }
            }

            HasFinalizer = hasFinalizer;

            if (!resetType)
                Initialize();
        }

        #endregion

        #region DynamicType overrides

        public override object AllocateObject(params object[] args) {
            return base.AllocateObject(PrependThis(args));
        }

        public override object AllocateObject(Dict dict, params object[] args) {
            return base.AllocateObject(dict, PrependThis(args));
        }

        public override Type GetTypesToExtend(out IList<Type> interfacesToExtend) {
            interfacesToExtend = new List<Type>();
            foreach (object b in bases) {
                if (b is OldClass) continue;

                DynamicType baseType = b as DynamicType;
                IList<Type> baseTypeInterfaces;
                baseType.GetTypesToExtend(out baseTypeInterfaces);
                foreach (Type baseTypeInterface in baseTypeInterfaces)
                    interfacesToExtend.Add(baseTypeInterface);
            }
            // We dont use type.GetInterfaces() as it contains all the interfaces that are added by NewTypeMaker,
            // as well as all the interfaces implemented by type.BaseType. Instead, we only want the new set of
            // interfaces that need to be implemented by the new instance type.
            Debug.Assert(interfacesToExtend.Count < type.GetInterfaces().Length);

            // "type" is the instance type used for instances of this type. This will be a type created by NewTypeMaker. 
            // It's base type is either system.object, some Python type (Dict, List, etc...), some slots type (derivied
            // from it's parent type + some properties that expose the slot).  In order to support re-assignment to
            // class he two types need to share this underlying type which means they have the same layout in memory.  
            // When extending we therefore typically want to extend the common type, allowing the __class__ assignment
            // from one type to another.  If we're a type that defines __slots__ though we want to extend ourselves
            // as we make our object layout unique.

            Debug.Assert(NewTypeMaker.IsInstanceType(type));

            if (HasSlots) return type;
            return type.BaseType;
        }

        private void EnsureNewStyleBase(Tuple bases) {
            bool newBasesIncludeNewStyleClass = false;

            foreach (object baseType in bases) {
                if (!(baseType is OldClass))
                    newBasesIncludeNewStyleClass = true;
            }

            if (!newBasesIncludeNewStyleClass)
                throw Ops.TypeError("new-style class {0} can't have only classic bases", this);
        }

        public override Tuple BaseClasses {
            [PythonName("__bases__")]
            get { return bases; }

            [PythonName("__bases__")]
            set {
                EnsureNewStyleBase(value);

                // Ensure that the MRO is legal
                CalculateMro(value);

                lock (this) {
                    InitializeUserType(value, dict, true);

                    // note: bases & MethodResolutionOrder are out of sync for a short period of time.  But because 
                    // the user cannot atomically read both  values at the same time, this is logically the same as the race 
                    // happening in between those reads.  The important thing is that bases.
                    // Same thing for all of our __* methods that are cached w/ MethodWrappers.

                    ReinitializeHierarchy();
                }
            }
        }

        protected override string TypeCategoryDescription {
            get {
                return "user-defined class";
            }
        }

        #endregion

        #region DynamicType overrides

        public override string Repr(object self) {
            if (__repr__F.IsObjectMethod()) {
                return self.ToString();
            } else {
                object ret = __repr__F.Invoke(self);
                string strRet;
                if (ret != null && Converter.TryConvertToString(ret, out strRet)) return strRet;
                throw Ops.TypeError("__repr__ returned non-string type ({0})", Ops.GetDynamicType(ret).__name__);
            }
        }

        public override bool IsSubclassOf(object other) {
            ReflectedType rt = other as ReflectedType;
            if (rt != null) {
                if (type == rt.type || type.IsSubclassOf(rt.type))
                    return true;

                Type otherTypeToExtend = rt.GetTypeToExtend();
                if (otherTypeToExtend != null) {
                    if (type == otherTypeToExtend || type.IsSubclassOf(otherTypeToExtend))
                        return true;

                    foreach (Type interfaceType in type.GetInterfaces()) {
                        if (interfaceType == otherTypeToExtend)
                            return true;
                    }
                }

                return false;
            }

            if (this.Equals(other)) return true;

            foreach (IPythonType baseType in BaseClasses) {
                if (baseType.IsSubclassOf(other)) return true;
            }

            return false;
        }

        public override object GetAttr(ICallerContext context, object self, SymbolId name) {
            if (__getattribute__F.IsObjectMethod()) {
                object ret;
                if (TryBaseGetAttr(context, self, name, out ret)) {
                    return ret;
                } else {
                    throw Ops.AttributeError((string)SymbolTable.IdToString(name));
                }
            } else {
                return __getattribute__F.Invoke(self, SymbolTable.IdToString(name));
            }
        }

        public override bool TryGetAttr(ICallerContext context, object self, SymbolId name, out object ret) {
            if (__getattribute__F.IsObjectMethod()) {
                return TryBaseGetAttr(context, self, name, out ret);
            } else {
                try {
                    ret = __getattribute__F.Invoke(self, SymbolTable.IdToString(name));
                    return true;
                } catch (MissingMemberException) {
                    ret = null;
                    return false;
                }
            }
        }

        public override void SetAttr(ICallerContext context, object self, SymbolId name, object value) {
            if (__setattr__F.IsObjectMethod()) {
                BaseSetAttr(context, (ISuperDynamicObject)self, name, value);
            } else {
                __setattr__F.Invoke(self, SymbolTable.IdToString(name), value);
            }
        }

        public override void DelAttr(ICallerContext context, object self, SymbolId name) {
            if (__delattr__F.IsObjectMethod()) {
                BaseDelAttr(context, (ISuperDynamicObject)self, name);
            } else {
                __delattr__F.Invoke(self, SymbolTable.IdToString(name));
            }
        }

        internal override void BaseDelAttr(ICallerContext context, object self, SymbolId name) {
            object slot;
            if (!TryLookupSlot(context, name, out slot)) {
                IAttributesDictionary d = GetInitializedDict((ISuperDynamicObject)self);
                if (d != null && d.ContainsKey(name)) {
                    d.Remove(name);
                    return;
                } else {
                    if (name == SymbolTable.Class)
                        throw Ops.AttributeErrorForReadonlyAttribute(__name__.ToString(), name);
                    throw Ops.AttributeErrorForMissingAttribute(__name__.ToString(), name);
                }
            }

            if (!Ops.DelDescriptor(slot, self))
                throw Ops.AttributeErrorForReadonlyAttribute(__name__.ToString(), name);
        }

        public override List GetAttrNames(ICallerContext context, object self) {
            List baseNames = base.GetAttrNames(context, self);

            ISuperDynamicObject sdo = self as ISuperDynamicObject;
            if (sdo != null) {
                IAttributesDictionary dict = sdo.GetDict();
                if (dict != null) {
                    foreach (object o in dict.Keys) {
                        if (!baseNames.Contains(o)) baseNames.Add(o);
                    }
                }
            }

            return baseNames;
        }

        public override List GetAttrNames(ICallerContext context) {
            List res = base.GetAttrNames(context);
            if (HasDictionary) {
                res.AddNoLock("__dict__");
            }
            return res;
        }
        #endregion

        #region Object overrides

        [PythonName("__str__")]
        public override string ToString() {
            return string.Format("<class '{0}.{1}'>", __module__, __name__);
        }

        #endregion

        #region Internal implementation

        internal override bool TryBaseGetAttr(ICallerContext context, object o, SymbolId name, out object ret) {
            ISuperDynamicObject self = o as ISuperDynamicObject;

            if (name == SymbolTable.Dict) {
                ret = GetInitializedDict(self);
                return ret != null;
            }

            IAttributesDictionary d = self.GetDict();
            if (d != null) {
                if (d.TryGetValue(name, out ret)) {
                    return true;
                }
            }

            if (base.TryBaseGetAttr(context, o, name, out ret)) return true;

            if (name == SymbolTable.WeakRef && !HasSlots) {
                ret = null; return true;
            }

            if (!__getattr__F.IsObjectMethod()) {
                try {
                    ret = __getattr__F.Invoke(self, SymbolTable.IdToString(name));
                    return true;
                } catch (MissingMemberException) {
                    return false;
                }
            }

            return false;
        }

        internal override void BaseSetAttr(ICallerContext context, object self, SymbolId name, object value) {
            object slot;
            if (TryLookupSlot(context, name, out slot)) {
                if (Ops.SetDescriptor(slot, self, value)) return;
            }

            if (name == SymbolTable.Class) {
                // check that this is a legal new class
                UserType newType = value as UserType;
                if (newType == null) {
                    throw Ops.TypeError("__class__ must be set to new-style class, not '{0}' object", Ops.GetDynamicType(value).__name__);
                }
                if (newType.type != this.type) {
                    throw Ops.TypeErrorForIncompatibleObjectLayout("__class__ assignment", this, newType.type);
                }
                ((ISuperDynamicObject)self).SetDynamicType(newType);
                return;
            }

            if (name == SymbolTable.WeakRef)
                throw Ops.AttributeErrorForReadonlyAttribute(__name__.ToString(), SymbolTable.WeakRef);

            IAttributesDictionary d = GetInitializedDict((ISuperDynamicObject)self);
            if (d == null) throw Ops.AttributeErrorForMissingAttribute((string)__name__, name);

            d[name] = value;
        }

        #endregion

        #region Private implementation details

        internal bool HasSlots {
            get {
                return (flags & UserTypeFlags.HasSlots) != 0;
            }
            set {
                if (value)
                    flags |= UserTypeFlags.HasSlots;
                else
                    flags &= ~(UserTypeFlags.HasSlots);
            }
        }

        internal bool HasDictionary {
            get {
                return (flags & UserTypeFlags.HasDictionary) != 0;
            }
            set {
                if (value)
                    flags |= UserTypeFlags.HasDictionary;
                else
                    flags &= ~(UserTypeFlags.HasDictionary);
            }
        }

        internal bool HasWeakRef {
            get {
                return (flags & UserTypeFlags.HasWeakRef) != 0;
            }
            set {
                if (value)
                    flags |= UserTypeFlags.HasWeakRef;
                else
                    flags &= ~(UserTypeFlags.HasWeakRef);
            }
        }

        protected override bool HasFinalizer {
            get {
                return (flags & UserTypeFlags.HasFinalizer) != 0;
            }
            set {
                if (value)
                    flags |= UserTypeFlags.HasFinalizer;
                else
                    flags &= ~(UserTypeFlags.HasFinalizer);
            }
        }

        private NamespaceDictionary CreateNamespaceDictionary(IDictionary<object, object> dict) {
            string[] names = (string[])this.type.GetField(NewTypeMaker.VtableNamesField).GetValue(null);
            SymbolId[] symNames = new SymbolId[names.Length];
            for (int i = 0; i < symNames.Length; i++) {
                symNames[i] = SymbolTable.StringToId(names[i]);
            }
            NamespaceDictionary ret = NamespaceDictionary.Make(symNames, bases);

            foreach (KeyValuePair<object, object> kv in dict) {
                PythonFunction func = kv.Value as PythonFunction;
                if (func != null) {
                    if (func.Name != "__new__") {
                        ret.AsObjectKeyedDictionary()[kv.Key] = new Method(func, null, this);
                    } else {
                        ret.AsObjectKeyedDictionary()[kv.Key] = new StaticMethod(func);
                    }
                } else {
                    ret.AsObjectKeyedDictionary()[kv.Key] = kv.Value;
                }
            }
            return ret;
        }

        /// <summary>
        /// If we have only interfaces, we'll need to insert object's base
        /// </summary>
        private static Tuple EnsureBaseType(Tuple bases) {
            foreach (object baseClass in bases) {
                if (baseClass is OldClass) continue;

                ReflectedType reflectedBaseType = baseClass as ReflectedType;
                if (reflectedBaseType == null || !reflectedBaseType.GetTypeToExtend().IsInterface) {
                    // Found a concrete (non-interface) type. We are done.
                    return bases;
                }

            }

            // We found only interfaces. We need do add System.Object to the bases
            return new Tuple(bases, TypeCache.Object);
        }

        private static IAttributesDictionary GetInitializedDict(ISuperDynamicObject self) {
            IAttributesDictionary d = self.GetDict();
            if (d == null) {
                d = new FieldIdDict();
                if (!self.SetDict(d)) return null;
            }
            return d;
        }

        #endregion

        #region IDynamicObject Members

        public override DynamicType GetDynamicType() {
            return TypeCache.UserType;
        }

        #endregion

        #region IRichEquality helpers

        public static object RichGetHashCodeHelper(object self) {
            // new-style classes only lookup in slots, not in instance
            // members
            object func;
            if (Ops.GetDynamicType(self).TryLookupBoundSlot(DefaultContext.Default, self, SymbolTable.Hash, out func)) {
                return Converter.ConvertToInt32(Ops.Call(func));
            }
            return Ops.NotImplemented;
        }

        public static object RichEqualsHelper(object self, object other) {
            return InternalCompare(SymbolTable.OpEqual, self, other);
        }

        public static object RichNotEqualsHelper(object self, object other) {
            return InternalCompare(SymbolTable.OpNotEqual, self, other);
        }
        #endregion

        #region IRichComparable Helpers
        public static object CompareToHelper(object self, object other) {

            DynamicType selfType = Ops.GetDynamicType(self);
            DynamicType otherType = Ops.GetDynamicType(other);

            object res;
            if (selfType == otherType) {
                // try __cmp__ first if it's defined.
                res = InternalCompare(SymbolTable.Cmp, self, other);
                if (res != Ops.NotImplemented) return res;

                // no need to check the other side - the types are identical,
                // and we don't look on instances for new-style classes.
            }

            // next try equals, return 0 if we match.
            res = RichEqualsHelper(self, other);
            if (res != Ops.NotImplemented) {
                if (Ops.IsTrue(res)) return 0;
            } else if (other != null) {
                // try the reverse
                res = RichEqualsHelper(other, self);
                if (res != Ops.NotImplemented && Ops.IsTrue(res)) return 0;
            }

            // next try less than
            res = LessThanHelper(self, other);
            if (res != Ops.NotImplemented) {
                if (Ops.IsTrue(res)) return -1;
            } else if (other != null) {
                // try the reverse
                res = GreaterThanHelper(other, self);
                if (res != Ops.NotImplemented && Ops.IsTrue(res)) return -1;
            }

            // finally try greater than
            res = GreaterThanHelper(self, other);
            if (res != Ops.NotImplemented) {
                if (Ops.IsTrue(res)) return 1;
            } else if (other != null) {
                //and the reverse
                res = LessThanHelper(other, self);
                if (res != Ops.NotImplemented && Ops.IsTrue(res)) return 1;
            }

            if (selfType != otherType) {
                // finally try __cmp__ if our types are different
                res = InternalCompare(SymbolTable.Cmp, self, other);
                if (res != Ops.NotImplemented) return res;
            }

            return Ops.NotImplemented;
        }

        public static object GreaterThanHelper(object self, object other) {
            return InternalCompare(SymbolTable.OpGreaterThan, self, other);
        }

        public static object LessThanHelper(object self, object other) {
            return InternalCompare(SymbolTable.OpLessThan, self, other);
        }

        public static object GreaterThanOrEqualHelper(object self, object other) {
            return InternalCompare(SymbolTable.OpGreaterThanOrEqual, self, other);
        }

        public static object LessThanOrEqualHelper(object self, object other) {
            return InternalCompare(SymbolTable.OpLessThanOrEqual, self, other);
        }

        private static object InternalCompare(SymbolId cmp, object self, object other) {
            return Ops.GetDynamicType(self).InvokeSpecialMethod(cmp, self, other);
        }

        #endregion

        #region Object Override helpers

        /// <summary>
        /// Object.ToString() displays the CLI type name.  But we want to display the class name (e.g.
        /// '<foo object at 0x000000000000002C>' unless we've overridden __repr__ but not __str__ in 
        /// which case we'll display the result of __repr__.
        /// </summary>
        public static string ToStringHelper(ISuperDynamicObject o) {

            object ret;
            UserType ut = o.GetDynamicType() as UserType;
            if (ut.TryLookupBoundSlot(DefaultContext.Default, o, SymbolTable.Repr, out ret)) {
                string strRet;
                if (ret != null && Converter.TryConvertToString(Ops.Call(Ops.GetDescriptor(ret, o, ut)), out strRet)) return strRet;
                throw Ops.TypeError("__repr__ returned non-string type ({0})", Ops.GetDynamicType(ret).__name__);
            }

            return DynamicType.ReprMethod(o).ToString();
        }

        public static string ToStringReturnHelper(object o) {
            if (o is string && o != null) {
                return (string)o;
            }
            throw Ops.TypeError("__str__ returned non-string type ({0})", Ops.GetDynamicType(o).__name__);
        }

        #endregion


        #region IWeakReferenceable Members

        WeakRefTracker IWeakReferenceable.GetWeakRef() {
            object res;
            if (dict.TryGetValue(SymbolTable.WeakRef, out res)) {
                return res as WeakRefTracker;
            }
            return null;
        }

        bool IWeakReferenceable.SetWeakRef(WeakRefTracker value) {
            dict[SymbolTable.WeakRef] = value;
            return true;
        }

        void IWeakReferenceable.SetFinalizer(WeakRefTracker value) {
            ((IWeakReferenceable)this).SetWeakRef(value);
        }

        #endregion
    }

    public class BigNamespaceDictionary : NamespaceDictionary {
        int[] nonInheritMap;
        public BigNamespaceDictionary(SymbolId[] knownKeys, Tuple bases)
            : base(knownKeys, bases) {
        }

        protected override void SortKeys() {
            int[] sortMap = new int[keys.Length];
            for (int i = 0; i < sortMap.Length; i++) sortMap[i] = i;
            Array.Sort(keys, sortMap);
            // we want the inverse of the map we got
            nonInheritMap = new int[sortMap.Length];
            for (int i = 0; i < sortMap.Length; i++) {
                nonInheritMap[sortMap[i]] = i;
            }
        }

        public override bool TryGetExtraValue(SymbolId key, out object value) {
            int index = Array.BinarySearch(keys, key);
            if (index >= 0) {
                value = values[index];
                return value != Uninitialized.instance && !isInherited[index]; //isInherited???
            }
            value = null;
            return false;
        }

        public override bool TrySetExtraValue(SymbolId key, object value) {
            int index = Array.BinarySearch(keys, key);
            if (index >= 0) {
                values[index] = value;
                isInherited[index] = false;
                return true;
            }
            value = null;
            return false;
        }

        public override bool TryGetNonInheritedValue(int key, out object value) {
            return base.TryGetNonInheritedValue(nonInheritMap[key], out value);
        }
    }

    [PythonType(typeof(Dict))]
    public class NamespaceDictionary : CustomSymbolDict, ICloneable {
        const int BinarySearchSize = 32;

        internal new SymbolId[] keys;
        internal new object[] values;
        internal bool[] isInherited;     // inheritance directly from CLS type, not from another UserType.
        internal Tuple bases;

        public static NamespaceDictionary Make(SymbolId[] knownKeys, Tuple bases) {
            if (knownKeys.Length > BinarySearchSize) {
                return new BigNamespaceDictionary(knownKeys, bases);
            } else {
                return new NamespaceDictionary(knownKeys, bases);
            }
        }

        public NamespaceDictionary() {
            keys = new SymbolId[0];
            values = Ops.EMPTY;
        }

        protected NamespaceDictionary(SymbolId[] knownKeys, Tuple bases)
            : this(knownKeys) {
            this.bases = bases;
            for (int i = 0; i < bases.Count; i++) {
                UserType ut = bases[i] as UserType;
                if (ut != null) {
                    NamespaceDictionary nd = ut.dict as NamespaceDictionary;
                    if (nd != null) {
                        PropagateKeys(nd);
                    }
                }
            }
        }

        private NamespaceDictionary(SymbolId[] knownKeys)
            : base() {
            this.keys = knownKeys;

            SortKeys();

            int N = knownKeys.Length;
            this.values = new object[N];
            this.isInherited = new bool[N];
            for (int i = 0; i < N; i++) isInherited[i] = true;
        }

        public override SymbolId[] GetExtraKeys() {
            int count = 0;
            for (int i = 0; i < values.Length; i++) {
                if (!isInherited[i] && values[i] != Uninitialized.instance) count++;
            }
            SymbolId[] ret = new SymbolId[count];
            count = 0;
            for (int i = 0; i < values.Length; i++) {
                if (!isInherited[i] && values[i] != Uninitialized.instance) ret[count++] = keys[i];
            }
            return ret;
        }

        public override bool TrySetExtraValue(SymbolId key, object value) {
            SymbolId[] ks = keys;
            for (int i = 0; i < ks.Length; i++) {
                if (ks[i].Equals(key)) {
                    values[i] = value;
                    isInherited[i] = false;
                    return true;
                }
            }
            return false;
        }

        public override bool TryGetExtraValue(SymbolId key, out object value) {
            SymbolId[] ks = keys;
            for (int i = 0; i < ks.Length; i++) {
                if (ks[i].Equals(key)) {
                    value = values[i];
                    return value != Uninitialized.instance && !isInherited[i];
                }
            }
            value = null;
            return false;
        }

        /// <summary> Called from generated code for base class call </summary>
        public virtual bool TryGetNonInheritedValue(int key, out object value) {
            if (isInherited[key]) {
                value = null;
                return false;
            } else {
                value = values[key];
                return value != Uninitialized.instance;
            }
        }

        protected virtual void SortKeys() {
        }

        private void PropagateKeys(NamespaceDictionary nd) {
            for (int i = 0; i < keys.Length && i < nd.keys.Length; i++) {
                if (nd.keys[i] == keys[i]) {
                    // common case: our layout matches our parents
                    PropagateInheritedKey(i, i, nd);
                } else {
                    // uncommon case - multiple inheritance only (maybe?)
                    for (int j = 0; j < keys.Length; j++) {
                        if (nd.keys[i] == keys[j]) {
                            PropagateInheritedKey(i, j, nd);
                        }
                    }
                }
            }
        }

        private void PropagateInheritedKey(int from, int to, NamespaceDictionary parent) {
            if (!parent.isInherited[from] && isInherited[to]) {
                // our parent didn't has an override, so we get
                // their override too.  Both are user defined functions
                // so we clear isInherited for this slot.
                values[to] = parent.values[from];
                isInherited[to] = false;
            }
        }

        [PythonClassMethod("fromkeys")]
        public static object fromkeys(DynamicType cls, object seq) {
            return Dict.FromKeys(cls, seq, null);
        }

        [PythonClassMethod("fromkeys")]
        public static object fromkeys(DynamicType cls, object seq, object value) {
            return Dict.FromKeys(cls, seq, value);
        }
    }
}