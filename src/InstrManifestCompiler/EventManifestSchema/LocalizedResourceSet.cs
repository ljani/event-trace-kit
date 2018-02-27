namespace InstrManifestCompiler.EventManifestSchema
{
    using System.Globalization;
    using InstrManifestCompiler.Support;

    public sealed class LocalizedResourceSet : SourceItem
    {
        public LocalizedResourceSet(CultureInfo culture)
        {
            Culture = culture;
            Strings = new LocalizedStringCollection();
        }

        public CultureInfo Culture { get; }
        public LocalizedStringCollection Strings { get; }
    }
}
