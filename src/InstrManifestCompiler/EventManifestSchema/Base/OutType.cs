namespace InstrManifestCompiler.EventManifestSchema.Base
{
    using System.Xml;
    using System.Xml.Linq;

    internal sealed class OutType
    {
        public OutType(QName xmlType, bool isDefault)
        {
            XmlType = xmlType;
            IsDefault = isDefault;
        }

        public QName XmlType { get; }
        public bool IsDefault { get; }

        public static OutType Create(XElement elem, IXmlNamespaceResolver resolver)
        {
            QName xmlType = elem.GetQName("xmlType");
            bool isDefault = elem.GetOptionalBool("default", false);

            return new OutType(xmlType, isDefault);
        }
    }
}
