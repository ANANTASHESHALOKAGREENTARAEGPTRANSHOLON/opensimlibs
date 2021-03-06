///

using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;


using System.Runtime.InteropServices;


using Rednettle.Warp3D;

namespace Warp3D
{
	[Guid("8DF51A09-E883-4a55-AC8E-00508636BC22")]
	public interface ComClass1Interface
	{
		void Reset();
		void DisplayDefaultScene();
		bool CreateScene(int width, int height);
		bool Render();
		bool SetReflectivity(string name, int r);
		bool SetTransparency(string name, int t);
		bool SetTexture(string name, string path);
		bool SetBackgroundTexture(string path);
        bool SetBackgroundMaterial( string path );
		bool SetBackgroundColor(int color);
		bool SetAmbient(int c);
		bool RotateScene(float x, float y, float z);
		bool ScaleScene(float x, float y, float z);
		bool TranslateScene(float x, float y, float z);
		bool RotateObject(string name, float x, float y, float z);
        bool RotateSelf( string name, float x, float y, float z );
        bool RotateSelf( string name, warp_Matrix m );
        bool ScaleObject( string name, float s );
		bool SetObjectMaterial(string name, string m);
		bool SetEnvMap(string name, string path);
		bool AddLight(string name, float x, float y, float z, int color, int d, int s);
        Hashtable Import3Ds( string name, string path, bool addtoscene );
		bool NormaliseScene();
		bool AddMaterial(string name);
        bool AddMaterial( string name, int color );
        bool AddMaterial( string name, string path );
        bool SetWireframe( string name, bool w );
        bool AddLensFlare( string name );
        bool ProjectFrontal( string name );
        bool ProjectCylindric( string name );
        bool ShiftObject( string name, float x, float y, float z );
        bool AddSphere( string name, float radius, int segments );
        bool AddCube( string name, float size );
		bool AddBox( string name, float x, float y, float z );
        bool AddPlane( string name, float size );
        bool ScaleModel( string name, float scale );
        bool TranslateModel( string name, float x, float y, float z );
        bool RotateModel( string name, float x, float y, float z );
        bool RotateModelSelf( string name, float x, float y, float z );
        bool RotateScene( warp_Quaternion quat, float x, float y, float z );
        void ShiftDefaultCamera( float x, float y, float z );
		bool SetPos( string name, float x, float y, float z );
    }

	[Guid("C0E6A7B1-E5FE-4b48-A310-B05048A925EA"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
	public interface ComClass1Events
	{
	}

	[Guid("717EA9B1-4E48-4a1d-BCF0-055CAE146598"), ClassInterface(ClassInterfaceType.None), ComSourceInterfaces(typeof(ComClass1Events))]
	public partial class Warp3D : UserControl, ComClass1Interface
	{
		warp_Scene _scene = null;
		string _waiting = "Waiting...";

        private Hashtable _plugins = new Hashtable();
        private Hashtable _models = new Hashtable();

		bool _dragging = false;
		int _oldx = 0;
		int _oldy = 0;
        bool _controlkey = false;
        public KeyEventArgs _keargs;

		public Warp3D()
		{
			InitializeComponent();

			this.MouseUp += new MouseEventHandler(OnMouseUp);
			this.MouseDown += new MouseEventHandler(OnMouseDown);
			this.MouseMove += new MouseEventHandler(OnMouseMove);
		}

        //FIX: Added Scene property so it could be consumed outside of the control. - Created by: X
        public warp_Scene Scene
        {
            get { return _scene; }
            set { _scene = value; }
        }
	       // - Created by: X

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			Graphics g = e.Graphics;

			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
			g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

			if (_scene != null)
			{
				g.DrawImage(_scene.getImage(), 0, 0, this.Width, this.Height);
			}
			else
			{
				Font drawFont = new Font("Verdana", 8);

				SolidBrush drawBrush = new SolidBrush(Color.Black);
				RectangleF drawRect = new RectangleF(-1, -1, Width + 1, Height + 1);

				e.Graphics.FillRectangle( new SolidBrush(this.BackColor), drawRect);
				e.Graphics.DrawString( _waiting, drawFont, drawBrush, drawRect);
			}
		}

		public void OnMouseMove(object sender, MouseEventArgs e)
		{
			if (_dragging)
			{
				float dx = (float)(e.Y - _oldy) / 50;
				float dy = (float)(_oldx - e.X) / 50;

                if ( _controlkey == true )
                {
                    _scene.defaultCamera.shift( 0, 0, dx );
                }
                else
                {
                    _scene.rotate( dx, dy, 0 );
                }


                _scene.render();

				_oldx = e.X;
				_oldy = e.Y;

				Refresh();
			}
		}

		public void OnMouseUp(object sender, MouseEventArgs e)
		{
			_dragging = false;
		}

		public void OnMouseDown(object sender, MouseEventArgs e)
		{
			_oldx = e.X;
			_oldy = e.Y;

			_dragging = true;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
		}

        public bool RegisterPlugIN( string name, warp_FXPlugin plugin )
        {
            if ( _scene == null )
            {
                return false;
            }

            _plugins.Add( name, plugin );

            return true;
        }

        public void ShiftDefaultCamera( float x, float y, float z )
        {
            _scene.defaultCamera.shift( x, y, z );
        }

        public bool AddSphere( string name, float radius, int segments )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = warp_ObjectFactory.SPHERE(radius, segments);

            if( o == null )
            {
                return false;
            }

            _scene.addObject( name, o );
			_scene.rebuild();

            return true;
        }

        public bool AddPlane( string name, float size  )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = warp_ObjectFactory.SIMPLEPLANE(size, true);

            if ( o == null )
            {
                return false;
            }

            _scene.addObject( name, o );

            return true;
        }

        public bool AddCube( string name, float size )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = warp_ObjectFactory.CUBE( size );

            if ( o == null )
            {
                return false;
            }

            _scene.addObject( name, o );
			_scene.rebuild();

            return true;
        }

		public bool AddBox( string name, float x, float y, float z )
		{
			if ( _scene == null )
			{
				return false;
			}

			warp_Object o = warp_ObjectFactory.BOX( x, y, z );

			if ( o == null )
			{
				return false;
			}

			_scene.addObject( name, o );
			_scene.rebuild();

			return true;
		}


        public bool ProjectFrontal( string name )
        {
            if ( _scene == null )
            {
                return false;
            }

			warp_Object o = _scene.sceneobject(name);
			if(o == null)
			{
				return false;
			}

            warp_TextureProjector.projectFrontal( o );

            return true;
        }

        public bool ProjectCylindric( string name )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = _scene.sceneobject( name );
            if ( o == null )
            {
                return false;
            }

            warp_TextureProjector.projectCylindric( o );

            return true;
        }

        public bool ShiftObject( string name, float x, float y, float z )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = _scene.sceneobject( name );
            if ( o == null )
            {
                return false;
            }

			o.shift( x, y, z );

            return true;
        }

		public bool SetPos( string name, float x, float y, float z )
		{
			if ( _scene == null )
			{
				return false;
			}

			warp_Object o = _scene.sceneobject( name );
			if ( o == null )
			{
				return false;
			}

			o.setPos( x, y, z );

			return true;
		}


        public bool AddLensFlare( string name )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_FXLensFlare lensFlare = new warp_FXLensFlare( name, _scene, false );
			lensFlare.preset1();

            RegisterPlugIN( name, lensFlare );

            return true;
        }

		public bool NormaliseScene()
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.normalize();

			return true;
		}

		public bool SetAmbient(int c)
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.setAmbient(c);

			return true;
		}

        public bool RotateScene( warp_Quaternion quat, float x, float y, float z )
        {
            if ( _scene == null )
            {
                return false;
            }

            _scene.rotate( quat,  x,  y,  z );

            return true;
        }

		public bool RotateScene(float x, float y, float z)
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.rotate(x, y, z);

			return true;
		}

        public bool RotateScene( warp_Matrix m )
        {
            if ( _scene == null )
            {
                return false;
            }

            _scene.rotate( m );

            return true;
        }

		public bool ScaleScene(float x, float y, float z)
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.scale(x, y, z);

			return true;
		}

		public bool TranslateScene(float x, float y, float z)
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.shift(x, y, z);

			return true;
		}


		public bool RotateObject(string name, float x, float y, float z)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Object o = _scene.sceneobject(name);
			if(o == null)
			{
				return false;
			}

			o.rotate(x, y, z);

			return true;
		}

        public bool RotateSelf( string name, float x, float y, float z )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = _scene.sceneobject( name );
            if ( o == null )
            {
                return false;
            }

            o.rotateSelf( x, y, z );

            return true;
        }

        public bool RotateSelf( string name, warp_Matrix m )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = _scene.sceneobject( name );
            if ( o == null )
            {
                return false;
            }

            o.rotateSelf( m );

            return true;
        }

        public bool RotateSelf( string name, warp_Quaternion quat )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = _scene.sceneobject( name );
            if ( o == null )
            {
                return false;
            }

            o.rotateSelf( quat );

            return true;
        }


        public bool ScaleObject( string name, float s )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Object o = _scene.sceneobject( name );
            if ( o == null )
            {
                return false;
            }

            o.scale( s );

            return true;
        }

		public bool SetObjectMaterial(string name, string m)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Material material = (warp_Material)_scene.materialData[m];
			if (material == null)
			{
				return false;
			}

			_scene.sceneobject(name).setMaterial(material);

			return true;
		}

		public bool SetEnvMap(string name, string path)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Texture texture = null;
			try
			{
				texture = new warp_Texture( path );
			}
			catch(Exception)
			{
				return false;
			}

			warp_Material material = (warp_Material)_scene.materialData[name];
			if (material == null)
			{
				return false;
			}

			material.setEnvmap(texture);

			return true;
		}

		public bool AddLight(string name, float x, float y, float z, int color, int d, int s)
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.addLight(name,new warp_Light(new warp_Vector(x,y,z),color,d,s));

			return true;
		}

        public bool RotateModelSelf( string name, float x, float y, float z )
        {
            if ( _scene == null )
            {
                return false;
            }

            Hashtable model = ( Hashtable )_models[ name ];
            if ( model == null )
            {
                return false;
            }

            foreach ( DictionaryEntry myDE in model )
            {
                string key = ( string )myDE.Key;
                warp_Object o = ( warp_Object )myDE.Value;

                o.rotateSelf( x, y, z );
            }

            return true;
        }

        public bool RotateModel( string name, float x, float y, float z )
        {
            if ( _scene == null )
            {
                return false;
            }

            Hashtable model = ( Hashtable )_models[ name ];
            if ( model == null )
            {
                return false;
            }

            foreach ( DictionaryEntry myDE in model )
            {
                string key = ( string )myDE.Key;
                warp_Object o = ( warp_Object )myDE.Value;

                o.rotate( x, y, z );
            }

            return true;
        }

        public bool TranslateModel( string name, float x, float y, float z )
        {
            if ( _scene == null )
            {
                return false;
            }

            Hashtable model = ( Hashtable )_models[ name ];
            if ( model == null )
            {
                return false;
            }

            foreach ( DictionaryEntry myDE in model )
            {
                string key = ( string )myDE.Key;
                warp_Object o = ( warp_Object )myDE.Value;

                o.shift( x, y, z );
            }

            return true;
        }

        public bool ScaleModel( string name, float scale )
        {
            if ( _scene == null )
            {
                return false;
            }

            Hashtable model = (Hashtable)_models[ name ];
            if ( model == null )
            {
                return false;
            }

            foreach ( DictionaryEntry myDE in model )
            {
                string key = ( string )myDE.Key;
                warp_Object o = ( warp_Object )myDE.Value;

                o.scaleSelf( scale );
            }

            return true;
        }

		public Hashtable Import3Ds(string name, string path, bool addtoscene)
		{
			if (_scene == null)
			{
				return null;
			}

            Hashtable list = null;
			warp_3ds_Importer studio = new warp_3ds_Importer();
			try
			{
				list = studio.importFromFile( name, path );

                if ( addtoscene )
                {
                    foreach ( DictionaryEntry myDE in list )
                    {
                        string key = (string)myDE.Key;
                        warp_Object o = (warp_Object)myDE.Value;

                        _scene.addObject( key , o );
                    }
                }

                _scene.rebuild();
                _models.Add( name, list );
			}
			catch(Exception)
			{
				return null;
			}

			return list;
		}

		public bool SetBackgroundColor(int c)
		{
			if (_scene == null)
			{
				return false;
			}

			_scene.environment.bgcolor = c;

			return true;
		}

		public bool SetBackgroundTexture(string path)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Texture texture = null;
			try
			{
				texture = new warp_Texture(path);
			}
			catch (Exception)
			{
				return false;
			}

			_scene.environment.setBackground(texture);

			return true;
		}

        public bool SetBackgroundMaterial( string path )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Material material = null;
            try
            {
                material = new warp_Material( path );
            }
            catch ( Exception )
            {
                return false;
            }

            warp_Texture texture = material.getTexture();
            if ( texture == null )
            {
                return false;
            }

            _scene.environment.setBackground( texture );

            return true;
        }

        public bool SetWireframe( string name, bool w )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Material material = ( warp_Material )_scene.materialData[ name ];
            if ( material == null )
            {
                return false;
            }

            material.setWireframe( w );

            return true;
        }

		public bool SetTexture(string name, string path)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Texture texture = null;
			try
			{
				texture = new warp_Texture( path );
			}
			catch(Exception)
			{
				return false;
			}

			warp_Material material = (warp_Material)_scene.materialData[name];
			if (material == null)
			{
				return false;
			}

			material.setTexture(texture);

			return true;
		}

		public bool AddMaterial(string name)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Material material = new warp_Material();
			_scene.addMaterial(name, material);

			return true;
		}

        public bool AddMaterial( string name, int color )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Material material = new warp_Material( color );
            _scene.addMaterial( name, material );

            return true;
        }

        public bool AddMaterial( string name, string path )
        {
            if ( _scene == null )
            {
                return false;
            }

            warp_Material material = null;
            try
            {
                material = new warp_Material( path );
            }
            catch ( Exception )
            {
                return false;
            }

            _scene.addMaterial( name, material );

            return true;
        }

        public bool SetReflectivity( string name, int r )
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Material material = (warp_Material)_scene.materialData[name];
			if (material == null)
			{
				return false;
			}

			material.setReflectivity(r);

			return true;
		}

		public bool SetTransparency(string name, int t)
		{
			if (_scene == null)
			{
				return false;
			}

			warp_Material material = (warp_Material)_scene.materialData[name];
			if (material == null)
			{
				return false;
			}

			material.setTransparency(t);

			return true;
		}

		public bool Render()
		{
			try
			{
				_scene.render();

                foreach ( DictionaryEntry myDE in _plugins )
                {
                    warp_FXPlugin plugin = ( warp_FXPlugin )myDE.Value;
                    plugin.apply();
                }
			}
			catch (Exception)
			{
				return false;
			}

			return true;
		}

		public bool CreateScene(int width, int height)
		{
			try
			{
				_scene = new warp_Scene(width, height);

                _plugins.Clear();
                _models.Clear();
			}
			catch (Exception)
			{
				Reset();
				return false;
			}

			return true;
		}

		public void Reset()
		{
			_scene = null;
			System.GC.Collect();
		}

		public void DisplayDefaultScene()
		{
			_scene = new warp_Scene(512, 512);

			warp_Material crystal = new warp_Material(warp_TextureFactory.MARBLE(128, 128, .15f));
			_scene.addMaterial("crystal", crystal);

			warp_Material c = (warp_Material)_scene.materialData["crystal"];
			c.setReflectivity(255);
			c.setTransparency(100);

			_scene.environment.setBackground(warp_TextureFactory.CHECKERBOARD(128,128,3,0x000000,0x999999));

			_scene.addLight("light1", new warp_Light(new warp_Vector(0.2f, 0.2f, 1f), 0xFFFFFF, 320, 80));
			_scene.addLight("light2", new warp_Light(new warp_Vector(-1f, -1f, 1f), 0xffffff, 100, 40));

			warp_Vector[] path = new warp_Vector[15];

			path[0] = new warp_Vector(0.0f, 0.2f, 0);
			path[1] = new warp_Vector(0.13f, 0.25f, 0);
			path[2] = new warp_Vector(0.33f, 0.3f, 0);
			path[3] = new warp_Vector(0.43f, 0.6f, 0);
			path[4] = new warp_Vector(0.48f, 0.9f, 0);
			path[5] = new warp_Vector(0.5f, 0.9f, 0);
			path[6] = new warp_Vector(0.45f, 0.6f, 0);
			path[7] = new warp_Vector(0.35f, 0.3f, 0);
			path[8] = new warp_Vector(0.25f, 0.2f, 0);
			path[9] = new warp_Vector(0.1f, 0.15f, 0);
			path[10] = new warp_Vector(0.1f, 0.0f, 0);
			path[11] = new warp_Vector(0.1f, -0.5f, 0);
			path[12] = new warp_Vector(0.35f, -0.55f, 0);
			path[13] = new warp_Vector(0.4f, -0.6f, 0);
			path[14] = new warp_Vector(0.0f, -0.6f, 0);

			_scene.addObject("wineglass", warp_ObjectFactory.ROTATIONOBJECT(path, 32));
			_scene.sceneobject("wineglass").setMaterial(_scene.material("crystal"));

			_scene.sceneobject("wineglass").scale(0.8f, 0.8f, 0.8f);
			_scene.sceneobject("wineglass").rotate(0.5f, 0f, 0f);

			_scene.render();

			Refresh();
		}

		private void Warp3D_Load(object sender, EventArgs e)
		{

		}

        private void Warp3D_KeyDown( object sender, KeyEventArgs e )
        {
            _keargs = e;
            _controlkey = e.Control;
        }

        private void Warp3D_KeyUp( object sender, KeyEventArgs e )
        {
            _keargs = e;
            _controlkey = e.Control;


        }
	}
}
