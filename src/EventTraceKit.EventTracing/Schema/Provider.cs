namespace EventTraceKit.EventTracing.Schema
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using EventTraceKit.EventTracing.Schema.Base;
    using EventTraceKit.EventTracing.Support;

    public sealed class Provider : SourceItem
    {
        private readonly List<EnableBit> enableBits = new List<EnableBit>();
        private uint channelValue = 16;

        public Provider(
            LocatedRef<string> name,
            LocatedVal<Guid> id,
            LocatedRef<string> symbol)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol));

            Name = name;
            Id = id;
            Symbol = symbol;

            Channels = new ChannelCollection(this);
            Levels = new LevelCollection(this);
            Opcodes = new OpcodeCollection(this);
            Tasks = new TaskCollection(this);
            Keywords = new KeywordCollection(this);
            Maps = new MapCollection(this);
            NamedQueries = new PatternMapCollection(this);
            Filters = new FilterCollection(this);
            Templates = new TemplateCollection(this);
            Events = new EventCollection(this);
        }

        public Provider(
            LocatedRef<string> name,
            LocatedVal<Guid> id,
            LocatedRef<string> symbol,
            LocalizedString message)
            : this(name, id, symbol)
        {
            Message = message;
        }

        public EventManifest Manifest { get; set; }
        public int Index { get; set; }
        public LocatedRef<string> Name { get; }
        public LocatedVal<Guid> Id { get; }
        public LocatedNullable<Guid> ControlGuid { get; set; }
        public LocatedRef<string> Symbol { get; }
        public LocatedRef<string> ResourceFileName { get; set; }
        public LocatedRef<string> MessageFileName { get; set; }
        public LocatedRef<string> ParameterFileName { get; set; }
        public LocalizedString Message { get; set; }
        public LocatedRef<string> Namespace { get; set; }

        public LocatedNullable<bool> IncludeNameInTraits { get; set; }
        public LocatedNullable<Guid> GroupGuid { get; set; }

        /// <summary>
        ///   Gets or sets whether the process name will be included in the
        ///   provider traits. This allows for easier distinction between
        ///   different processes using the same providers compared to process
        ///   IDs, because loader events require kernel tracing. The default is
        ///   <see langword="false"/>.
        /// </summary>
        /// <remarks>
        ///   The process name is the base name without file extension of the
        ///   executing process(e.g., "service2" for "Z:\path\service2.exe").
        ///   The trait type is 128, and the trait data is the null-terminated
        ///   UTF-8 encoded process name(e.g., "\x0C\x00\x80service2\x00").
        ///   This trait is an ETK-only extension not supported by MC or TDH.
        /// </remarks>
        public LocatedNullable<bool> IncludeProcessName { get; set; }

        public ChannelCollection Channels { get; }
        public LevelCollection Levels { get; }
        public OpcodeCollection Opcodes { get; }
        public TaskCollection Tasks { get; }
        public KeywordCollection Keywords { get; }
        public MapCollection Maps { get; }
        public PatternMapCollection NamedQueries { get; }
        public FilterCollection Filters { get; }
        public TemplateCollection Templates { get; }
        public EventCollection Events { get; }

        public IReadOnlyList<EnableBit> EnableBits => enableBits;

        public void PopulateEnableBits()
        {
            var lookup = new Dictionary<Tuple<int, ulong>, EnableBit>();
            int bitPosition = 0;
            foreach (var evt in Events) {
                int level = evt.LevelValue;
                ulong mask = evt.KeywordMask;
                var key = Tuple.Create(level, mask);

                if (!lookup.TryGetValue(key, out var bit)) {
                    bit = new EnableBit(bitPosition++, level, mask);
                    lookup.Add(key, bit);
                    enableBits.Add(bit);
                }

                evt.EnableBit = bit;
            }
        }

        public byte CreateChannelValue()
        {
            if (channelValue >= byte.MaxValue)
                throw new NotSupportedException("Too many channels specified.");

            while (Channels.Any(c => c.Value == channelValue))
                ++channelValue;

            return (byte)channelValue;
        }

        public IEnumerable<Opcode> GetAllOpcodes()
        {
            return Opcodes.Concat(Tasks.SelectMany(x => x.Opcodes));
        }
    }
}
