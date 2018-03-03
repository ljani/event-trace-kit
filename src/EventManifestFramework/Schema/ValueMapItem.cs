namespace EventManifestFramework.Schema
{
    using System;
    using EventManifestFramework.Support;

    public sealed class ValueMapItem : MapItem
    {
        public ValueMapItem(ValueMap map, LocatedVal<uint> value, LocalizedString message)
            : base(value, message)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public ValueMapItem(
            ValueMap map, LocatedVal<uint> value, LocatedRef<string> symbol,
            LocalizedString message)
            : base(value, message, symbol)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public override MapKind Kind => MapKind.ValueMap;
        public override IMap Map { get; }
    }
}
