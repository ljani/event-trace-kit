namespace InstrManifestCompiler.EventManifestSchema
{
    using System;
    using System.Diagnostics.Contracts;
    using InstrManifestCompiler.Support;

    public sealed class BitMapItem : MapItem
    {
        private readonly BitMap map;

        public BitMapItem(BitMap map, StructValue<uint> value, LocalizedString message)
            : base(value, message)
        {
            Contract.Requires<ArgumentNullException>(map != null);
            this.map = map;
        }

        public BitMapItem(
            BitMap map, StructValue<uint> value, RefValue<string> symbol,
            LocalizedString message)
            : base(value, message, symbol)
        {
            Contract.Requires<ArgumentNullException>(map != null);
            this.map = map;
        }

        public override IMap Map
        {
            get { return map; }
        }

        public override MapKind Kind
        {
            get { return MapKind.BitMap; }
        }
    }

    public sealed class BitMap : Map
    {
        public BitMap(RefValue<string> name)
            : base(name)
        {
        }

        public BitMap(RefValue<string> name, RefValue<string> symbol)
            : base(name, symbol)
        {
        }

        public override MapKind Kind
        {
            get { return MapKind.BitMap; }
        }

        public override void Accept(IProviderItemVisitor visitor)
        {
            visitor.VisitBitMap(this);
        }
    }
}