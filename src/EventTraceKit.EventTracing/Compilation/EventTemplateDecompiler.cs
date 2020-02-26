namespace EventManifestCompiler
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Xml;
    using System.Xml.Linq;
    using EventTraceKit.EventTracing;
    using EventTraceKit.EventTracing.Compilation.ResGen;
    using EventTraceKit.EventTracing.Internal.Extensions;
    using EventTraceKit.EventTracing.Internal.Native;
    using EventTraceKit.EventTracing.Schema;
    using EventTraceKit.EventTracing.Schema.Base;
    using EventTraceKit.EventTracing.Support;

    public class DecompilationOptions
    {
        public string InputModule { get; set; }
        public string InputEventTemplate { get; set; }
        public string InputMessageTable { get; set; }
        public string OutputManifest { get; set; }
    }

    public sealed class EventTemplateDecompiler
    {
        private readonly XNamespace ns = EventManifestSchema.Namespace;
        private readonly IDiagnostics diags;
        private readonly DecompilationOptions opts;

        public EventTemplateDecompiler(IDiagnostics diags, DecompilationOptions opts)
        {
            this.diags = diags ?? throw new ArgumentNullException(nameof(diags));
            this.opts = opts ?? throw new ArgumentNullException(nameof(opts));

            if (opts.InputModule is null && (opts.InputEventTemplate is null || opts.InputMessageTable is null))
                throw new ArgumentException("No input files", nameof(opts));
        }

        public bool Run()
        {
            string manifestFile = opts.OutputManifest;

            var metadata = EventManifestParser.LoadWinmeta(diags);
            var reader = new EventTemplateReader(diags, metadata);

            EventManifest manifest;
            IEnumerable<Message> messages;
            if (opts.InputModule != null) {
                string providerBinary = Path.GetFullPath(opts.InputModule);

                using var module = SafeModuleHandle.LoadImageResource(providerBinary);
                if (module.IsInvalid)
                    throw new Win32Exception();

                using (var stream = module.OpenResource(UnsafeNativeMethods.RT_MESSAGETABLE, 1))
                    messages = reader.ReadMessageTable(stream);

                using (var stream = module.OpenResource("WEVT_TEMPLATE", 1))
                    manifest = reader.ReadWevtTemplate(stream, messages);

                foreach (var provider in manifest.Providers) {
                    provider.ResourceFileName = providerBinary;
                    provider.MessageFileName = providerBinary;
                }
            } else {
                using (var stream = File.OpenRead(opts.InputMessageTable))
                    messages = reader.ReadMessageTable(stream);

                using (var stream = File.OpenRead(opts.InputEventTemplate))
                    manifest = reader.ReadWevtTemplate(stream, messages);
            }

            StripReservedMetadata(manifest, metadata);
            InferSymbols(manifest);
            StripDefaultMessageIds(manifest);

            XDocument doc = ToXml(manifest);
            var settings = new XmlWriterSettings {
                Indent = true,
                IndentChars = "  "
            };
            using var output = File.Create(manifestFile);
            using var writer = XmlWriter.Create(output, settings);
            doc.WriteTo(writer);

            return true;
        }

        private void StripReservedMetadata(EventManifest manifest, IEventManifestMetadata metadata)
        {
            foreach (var provider in manifest.Providers) {
                var channelIds = new HashSet<byte>(metadata.Channels.Select(x => x.Value.Value));
                var levelNames = new HashSet<QName>(metadata.Levels.Select(x => x.Name.Value));
                var taskNames = new HashSet<QName>(metadata.Tasks.Select(x => x.Name.Value));
                var opcodeNames = new HashSet<QName>(metadata.Opcodes.Select(x => x.Name.Value));
                var keywordNames = new HashSet<QName>(metadata.Keywords.Select(x => x.Name.Value));

                var messageSet = new HashSet<LocalizedString>();
                messageSet.UnionWith(from x in provider.Channels
                                     where x.Message != null && channelIds.Contains(x.Value.Value)
                                     select x.Message);
                messageSet.UnionWith(from x in provider.Levels
                                     where x.Message != null && levelNames.Contains(x.Name)
                                     select x.Message);
                messageSet.UnionWith(from x in provider.Tasks
                                     where x.Message != null && taskNames.Contains(x.Name)
                                     select x.Message);
                messageSet.UnionWith(from x in provider.Opcodes
                                     where x.Task == null && x.Message != null && opcodeNames.Contains(x.Name)
                                     select x.Message);
                messageSet.UnionWith(from x in provider.Keywords
                                     where x.Message != null && keywordNames.Contains(x.Name)
                                     select x.Message);

                foreach (var resourceSet in manifest.Resources)
                    resourceSet.Strings.RemoveAll(x => messageSet.Contains(x));

                provider.Channels.RemoveAll(x => channelIds.Contains(x.Value.Value));
                provider.Levels.RemoveAll(x => levelNames.Contains(x.Name));
                provider.Tasks.RemoveAll(x => taskNames.Contains(x.Name));
                provider.Opcodes.RemoveAll(x => opcodeNames.Contains(x.Name));
                provider.Keywords.RemoveAll(x => keywordNames.Contains(x.Name));
            }
        }

        private static Dictionary<TKey, int> GetCountLookup<T, TKey>(IEnumerable<T> collection, Func<T, TKey> keySelector)
        {
            return collection.GroupBy(keySelector).ToDictionary(x => x.Key, x => x.Count());
        }

        private void InferSymbols(EventManifest manifest)
        {
            foreach (var provider in manifest.Providers)
                InferSymbols(provider);
        }

        private void InferSymbols(Provider provider)
        {
            var symbolTable = new HashSet<string>();

            var taskCounts = GetCountLookup(
                provider.Events.Where(x => x.Task != null),
                x => x.Task.Name.Value.LocalName);

            var opcodeCounts = GetCountLookup(
                provider.Events.Where(x => x.Task == null && x.Opcode != null),
                x => x.Opcode.Name.Value.LocalName ?? string.Empty);

            foreach (var evt in provider.Events) {
                if (evt.Task != null) {
                    string taskName = evt.Task.Name.Value.LocalName;
                    if (taskCounts[taskName] == 1) {
                        evt.Symbol = GetUniqueSymbol(taskName, symbolTable);
                        continue;
                    }

                    if (evt.Opcode != null) {
                        string opcodeName = evt.Opcode.Name.Value.LocalName;
                        evt.Symbol = GetUniqueSymbol(taskName + "_" + opcodeName, symbolTable);
                        continue;
                    }
                } else if (evt.Opcode != null) {
                    string opcodeName = evt.Opcode.Name.Value.LocalName;
                    if (opcodeCounts[opcodeName] == 1) {
                        evt.Symbol = GetUniqueSymbol(opcodeName, symbolTable);
                        continue;
                    }
                }

                evt.Symbol = GetUniqueSymbol("Event" + evt.Value.Value, symbolTable);
            }
        }

        private static string GetUniqueSymbol(string symbol, HashSet<string> symbolTable)
        {
            symbol = SanitizeSymbol(symbol);

            if (symbolTable.Add(symbol))
                return symbol;

            for (int suffix = 1; ; ++suffix) {
                string suffixed = symbol + suffix;
                if (symbolTable.Add(suffixed))
                    return suffixed;
            }
        }

        private static string SanitizeSymbol(string symbol)
        {
            symbol = symbol.Replace('-', '_');
            if (symbol.Length > 0 && char.IsDigit(symbol[0]))
                symbol = "_" + symbol;
            return symbol;
        }

        private void StripDefaultMessageIds(EventManifest manifest)
        {
            var idGenerator = new StableMessageIdGenerator(new NullDiagnostics());
            foreach (var provider in manifest.Providers) {
                StripMessageId(provider, x => x.Message, x => idGenerator.CreateId(provider));
                foreach (var item in provider.Events)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Levels)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Channels)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Tasks)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Opcodes)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Keywords)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Filters)
                    StripMessageId(item, x => x.Message, x => idGenerator.CreateId(x, provider));
                foreach (var item in provider.Maps)
                    foreach (var mapItem in item.Items)
                        StripMessageId(mapItem, x => x.Message, x => idGenerator.CreateId(x, item, provider));
            }
        }

        private void StripMessageId<T>(
            T entity, Func<T, LocalizedString> messageSelector, Func<T, uint> createId)
        {
            if (entity == null)
                return;

            LocalizedString message = messageSelector(entity);
            if (message == null)
                return;

            if (message.Id != LocalizedString.UnusedId && message.Id == createId(entity))
                message.Id = LocalizedString.UnusedId;
        }

        private XDocument ToXml(EventManifest manifest)
        {
            var providersElem = new XElement(ns + "events");
            foreach (var provider in manifest.Providers)
                providersElem.Add(ToXml(provider));

            //providersElem.Add(ToMessageTableXml(manifest.Resources));

            XElement localizationElem = null;
            if (manifest.Resources.Count > 0)
                localizationElem = new XElement(ns + "localization", manifest.Resources.Select(ToXml));

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(
                    ns + "instrumentationManifest",
                    new XAttribute(XNamespace.Xmlns + "win", WinEventSchema.Namespace),
                    new XAttribute(XNamespace.Xmlns + "xs", "http://www.w3.org/2001/XMLSchema"),
                    new XElement(ns + "instrumentation", providersElem),
                    localizationElem));

            return doc;
        }

        private XElement ToMessageTableXml(IEnumerable<LocalizedResourceSet> resources)
        {
            var elem = new XElement(ns + "messageTable");
            foreach (var resourceSet in resources)
                elem.Add(resourceSet.Strings.Select(ToMessageXml));
            return elem;
        }

        private XElement ToXml(LocalizedResourceSet resourceSet)
        {
            if (resourceSet.Strings.Count == 0)
                return null;

            return new XElement(
                ns + "resources",
                new XAttribute("culture", resourceSet.Culture.Name),
                new XElement(ns + "stringTable", resourceSet.Strings.Select(ToXml)));
        }

        private XElement ToXml(LocalizedString ls)
        {
            return new XElement(
                ns + "string",
                new XAttribute("id", ls.Name.Value),
                new XAttribute("value", ls.Value.Value));
        }

        private XElement ToMessageXml(LocalizedString ls)
        {
            if (ls.Id == LocalizedString.UnusedId)
                return null;

            var elem = new XElement(
                ns + "message",
                new XAttribute("value", ls.Id),
                new XAttribute("message", "$(string." + ls.Name + ")"));
            if (ls.Symbol != null)
                elem.Add(new XAttribute("symbol", ls.Symbol));
            return elem;
        }

        private XElement ToXml(Provider provider)
        {
            var elem = new XElement(
                ns + "provider",
                new XAttribute("name", provider.Name.Value),
                new XAttribute("guid", provider.Id.Value.ToString("B")),
                new XAttribute("symbol", provider.Symbol.Value));
            if (provider.ResourceFileName != null)
                elem.Add(new XAttribute("resourceFileName", provider.ResourceFileName.Value));
            if (provider.MessageFileName != null)
                elem.Add(new XAttribute("messageFileName", provider.MessageFileName.Value));
            if (provider.ParameterFileName != null)
                elem.Add(new XAttribute("parameterFileName", provider.ParameterFileName.Value));
            AddOptionalMessage(elem, provider.Message);

            if (provider.Events.Count > 0)
                elem.Add(new XElement(ns + "events", provider.Events.Select(ToXml)));
            if (provider.Channels.Count > 0)
                elem.Add(new XElement(ns + "channels", provider.Channels.Select(ToXml)));
            if (provider.Levels.Count > 0)
                elem.Add(new XElement(ns + "levels", provider.Levels.Select(ToXml)));
            if (provider.Tasks.Count > 0)
                elem.Add(new XElement(ns + "tasks", provider.Tasks.Select(ToXml)));
            if (provider.Opcodes.Count > 0)
                elem.Add(new XElement(ns + "opcodes", provider.Opcodes.Select(ToXml)));
            if (provider.Keywords.Count > 0)
                elem.Add(new XElement(ns + "keywords", provider.Keywords.Select(ToXml)));
            if (provider.Maps.Count > 0)
                elem.Add(new XElement(ns + "maps", provider.Maps.Select(ToXml)));
            if (provider.Templates.Count > 0)
                elem.Add(new XElement(ns + "templates", provider.Templates.Select(ToXml)));
            if (provider.Filters.Count > 0)
                elem.Add(new XElement(ns + "filters", provider.Filters.Select(ToXml)));

            return elem;
        }

        private XElement ToXml(Event @event)
        {
            var elem = new XElement(
                ns + "event",
                new XAttribute("value", @event.Value));
            if (@event.Version != 0)
                elem.Add(new XAttribute("version", @event.Version));
            if (@event.Symbol != null)
                elem.Add(new XAttribute("symbol", @event.Symbol));
            if (@event.Channel != null)
                elem.Add(new XAttribute("channel", @event.Channel.Name.Value));
            if (@event.Level != null)
                elem.Add(new XAttribute("level", @event.Level.Name.Value.ToPrefixedString()));
            if (@event.Task != null)
                elem.Add(new XAttribute("task", @event.Task.Name.Value.ToPrefixedString()));
            if (@event.Opcode != null)
                elem.Add(new XAttribute("opcode", @event.Opcode.Name.Value.ToPrefixedString()));
            if (@event.Keywords.Count > 0)
                elem.Add(new XAttribute(
                    "keywords",
                    string.Join(" ", @event.Keywords.Select(k => k.Name.Value.ToPrefixedString()))));
            if (@event.Template != null)
                elem.Add(new XAttribute("template", @event.Template.Id.Value));
            if (@event.NotLogged != null)
                elem.Add(new XAttribute("notLogged", @event.NotLogged.GetValueOrDefault() ? "true" : "false"));
            AddOptionalMessage(elem, @event.Message);
            return elem;
        }

        private XElement ToXml(Channel channel)
        {
            var elem = new XElement(
                ns + "channel",
                new XAttribute("name", channel.Name),
                new XAttribute("type", ToXml(channel.Type)));
            if (channel.Value != null)
                elem.Add(new XAttribute("value", channel.Value));
            if (channel.Symbol != null)
                elem.Add(new XAttribute("symbol", channel.Symbol));
            AddOptionalMessage(elem, channel.Message);
            return elem;
        }

        private XElement ToXml(Level level)
        {
            var elem = new XElement(
                ns + "level",
                new XAttribute("name", level.Name.Value.ToPrefixedString()),
                new XAttribute("value", level.Value));
            if (level.Symbol != null)
                elem.Add(new XAttribute("symbol", level.Symbol));
            AddOptionalMessage(elem, level.Message);
            return elem;
        }

        private XElement ToXml(Task task)
        {
            var elem = new XElement(
                ns + "task",
                new XAttribute("name", task.Name.Value.ToPrefixedString()),
                new XAttribute("value", task.Value));
            if (task.Guid != null)
                elem.Add(new XAttribute("eventGUID", task.Guid.Value.ToString("B")));
            if (task.Symbol != null)
                elem.Add(new XAttribute("symbol", task.Symbol));
            AddOptionalMessage(elem, task.Message);
            elem.Add(task.Opcodes.Select(ToXml));
            return elem;
        }

        private XElement ToXml(Opcode opcode)
        {
            var elem = new XElement(
                ns + "opcode",
                new XAttribute("name", opcode.Name.Value.ToPrefixedString()),
                new XAttribute("value", opcode.Value));
            if (opcode.Symbol != null)
                elem.Add(new XAttribute("symbol", opcode.Symbol));
            AddOptionalMessage(elem, opcode.Message);
            return elem;
        }

        private XElement ToXml(Keyword keyword)
        {
            var elem = new XElement(
                ns + "keyword",
                new XAttribute("name", keyword.Name.Value.ToPrefixedString()),
                new XAttribute("mask", string.Format(CultureInfo.InvariantCulture, "0x{0:X16}", keyword.Mask.Value)));
            if (keyword.Symbol != null)
                elem.Add(new XAttribute("symbol", keyword.Symbol));
            AddOptionalMessage(elem, keyword.Message);
            return elem;
        }

        private XElement ToXml(Map map)
        {
            if (map.Kind == MapKind.BitMap)
                return ToXml((BitMap)map);
            if (map.Kind == MapKind.ValueMap)
                return ToXml((ValueMap)map);
            throw new ArgumentException("map");
        }

        private XElement ToXml(BitMap map)
        {
            var elem = new XElement(
                ns + "bitMap",
                new XAttribute("name", map.Name));
            if (map.Symbol != null)
                elem.Add(new XAttribute("symbol", map.Symbol));
            elem.Add(map.Items.Select(i => ToXml((BitMapItem)i)));
            return elem;
        }

        private XElement ToXml(ValueMap map)
        {
            var elem = new XElement(
                ns + "valueMap",
                new XAttribute("name", map.Name));
            if (map.Symbol != null)
                elem.Add(new XAttribute("symbol", map.Symbol));
            elem.Add(map.Items.Select(i => ToXml((ValueMapItem)i)));
            return elem;
        }

        private XElement ToXml(BitMapItem item)
        {
            var elem = new XElement(
                ns + "map",
                new XAttribute("value", string.Format(CultureInfo.InvariantCulture, "{0:X}", item.Value)));
            if (item.Symbol != null)
                elem.Add(new XAttribute("symbol", item.Symbol));
            AddOptionalMessage(elem, item.Message);
            return elem;
        }

        private XElement ToXml(ValueMapItem item)
        {
            var elem = new XElement(
                ns + "map",
                new XAttribute("value", item.Value));
            if (item.Symbol != null)
                elem.Add(new XAttribute("symbol", item.Symbol));
            AddOptionalMessage(elem, item.Message);
            return elem;
        }

        private XElement ToXml(Template template)
        {
            var elem = new XElement(
                ns + "template",
                new XAttribute("tid", template.Id));
            if (template.Name != null)
                elem.Add(new XAttribute("name", template.Name));
            elem.Add(template.Properties.Select(ToXml));
            return elem;
        }

        private XElement ToXml(Property property)
        {
            if (property.Kind == PropertyKind.Struct)
                return ToXml((StructProperty)property);
            return ToXml((DataProperty)property);
        }

        private XElement ToXml(StructProperty property)
        {
            var elem = new XElement(
                ns + "struct",
                new XAttribute("name", property.Name));
            elem.Add(property.Properties.Select(ToXml));
            return elem;
        }

        private XElement ToXml(DataProperty property)
        {
            var elem = new XElement(
                ns + "data",
                new XAttribute("name", property.Name),
                new XAttribute("inType", property.InType.Name.ToPrefixedString()));
            if (property.OutType != null)
                elem.Add(new XAttribute("outType", property.OutType.Name.ToPrefixedString()));
            if (property.Length.IsSpecified)
                elem.Add(new XAttribute("length", property.Length));
            if (property.Count.IsSpecified)
                elem.Add(new XAttribute("count", property.Count));
            return elem;
        }

        private XElement ToXml(Filter filter)
        {
            var elem = new XElement(
                ns + "filter",
                new XAttribute("name", filter.Name.Value.ToPrefixedString()),
                new XAttribute("value", filter.Value),
                new XAttribute("version", filter.Version));
            if (filter.Symbol != null)
                elem.Add(new XAttribute("symbol", filter.Symbol));
            AddOptionalMessage(elem, filter.Message);
            return elem;
        }

        private string ToXml(ChannelType type)
        {
            return type switch
            {
                ChannelType.Admin => "Admin",
                ChannelType.Operational => "Operational",
                ChannelType.Analytic => "Analytic",
                ChannelType.Debug => "Debug",
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }

        private static void AddOptionalMessage(XElement elem, LocalizedString message)
        {
            if (message != null)
                elem.Add(new XAttribute("message", "$(string." + message.Name + ")"));
        }
    }
}