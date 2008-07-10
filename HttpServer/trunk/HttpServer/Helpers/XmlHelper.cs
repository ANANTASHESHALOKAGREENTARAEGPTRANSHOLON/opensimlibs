using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace HttpServer.Helpers
{
    public static class XmlHelper
    {

        public static string Serialize(object value)
        {
            //These to lines are nessacary to get rid of the default namespaces.
            XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
            ns.Add("", "");

            // removing XML declaration, the default is false
            XmlWriterSettings xmlSettings = new XmlWriterSettings();
            xmlSettings.Indent = true;
            xmlSettings.IndentChars = "\t";
            xmlSettings.OmitXmlDeclaration = true;

            StringBuilder sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb, xmlSettings))
            {
                XmlSerializer serializer = new XmlSerializer(value.GetType());
                serializer.Serialize(writer, value, ns);
                return sb.ToString();
            }
        }
    }
}