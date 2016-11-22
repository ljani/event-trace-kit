namespace InstrManifestCompiler.EventManifestSchema
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Linq;
    using System.Xml.Schema;
    using System.Xml.XPath;
    using InstrManifestCompiler.Collections;
    using InstrManifestCompiler.EventManifestSchema.Base;
    using InstrManifestCompiler.Extensions;
    using InstrManifestCompiler.Support;

    internal static class IEventManifestParserExtensions
    {
        public static EventManifest ParseManifest(
            this IEventManifestParser parser, string filePath)
        {
            using (var input = File.OpenRead(filePath))
                return parser.ParseManifest(input, filePath);
        }

        public static IEventManifestMetadata ParseWinmeta(
            this IEventManifestParser parser, string filePath)
        {
            using (var input = File.OpenRead(filePath))
                return parser.ParseWinmeta(input, filePath);
        }

        public static IEventManifestMetadata ParseMetadata(
            this IEventManifestParser parser, string filePath)
        {
            using (var input = File.OpenRead(filePath))
                return parser.ParseMetadata(input, filePath);
        }
    }

    internal sealed class EventManifestParser : IEventManifestParser
    {
        private readonly IDiagnostics diags;
        private readonly IEventManifestSpecification winmetaSpec = new WinMetaManifestSpecification();
        private readonly string schemaPath;
        private readonly List<IEventManifestMetadata> metadata = new List<IEventManifestMetadata>();

        private IEventManifestSpecification manifestSpec;
        private IXmlNamespaceResolver nsResolver;
        private bool failedValidation;

        public EventManifestParser(IDiagnostics diags)
            : this(diags, null)
        {
        }

        public EventManifestParser(IDiagnostics diags, string schemaPath)
        {
            Contract.Requires<ArgumentNullException>(diags != null);
            this.diags = diags;
            this.schemaPath = schemaPath ?? FindSchema();

            manifestSpec = new DefaultEventManifestSpecification(diags);
        }

        public static EventManifestParser CreateWithWinmeta(
            IDiagnostics diags, string schemaPath = null, string winmetaPath = null)
        {
            Contract.Requires<ArgumentNullException>(diags != null);

            string sdkPath = WindowsSdkUtils.FindSdkPath();
            if (schemaPath == null && sdkPath != null)
                schemaPath = Path.Combine(sdkPath, "eventman.xsd");
            if (winmetaPath == null && sdkPath != null)
                winmetaPath = Path.Combine(sdkPath, "winmeta.xml");

            if (schemaPath == null || !File.Exists(schemaPath)) {
                diags.ReportError("eventman.xsd not found. No such file '{0}'.", schemaPath);
                if (schemaPath == null)
                    diags.ReportError("eventman.xsd not found.");
                else
                    diags.ReportError("eventman.xsd not found. No such file '{0}'.", schemaPath);
                return null;
            }
            if (winmetaPath == null || !File.Exists(winmetaPath)) {
                if (winmetaPath == null)
                    diags.ReportError("winmeta.xml not found.");
                else
                    diags.ReportError("winmeta.xml not found. No such file '{0}'.", winmetaPath);
                return null;
            }

            var parser = new EventManifestParser(diags, schemaPath);

            var metadata = parser.ParseWinmeta(winmetaPath);
            if (metadata == null) {
                diags.ReportError("Failed to load winmeta.xml from '{0}'.", winmetaPath);
                return null;
            }

            parser.AddMetadata(metadata);
            return parser;
        }

        public static IEventManifestMetadata LoadWinmeta(
            IDiagnostics diags, string schemaPath = null, string winmetaPath = null)
        {
            Contract.Requires<ArgumentNullException>(diags != null);

            string sdkPath = WindowsSdkUtils.FindSdkPath();
            if (schemaPath == null && sdkPath != null)
                schemaPath = Path.Combine(sdkPath, "eventman.xsd");
            if (winmetaPath == null && sdkPath != null)
                winmetaPath = Path.Combine(sdkPath, "winmeta.xml");

            if (schemaPath == null || !File.Exists(schemaPath)) {
                diags.ReportError("eventman.xsd not found. No such file '{0}'.", schemaPath);
                if (schemaPath == null)
                    diags.ReportError("eventman.xsd not found.");
                else
                    diags.ReportError("eventman.xsd not found. No such file '{0}'.", schemaPath);
                return null;
            }
            if (winmetaPath == null || !File.Exists(winmetaPath)) {
                if (winmetaPath == null)
                    diags.ReportError("winmeta.xml not found.");
                else
                    diags.ReportError("winmeta.xml not found. No such file '{0}'.", winmetaPath);
                return null;
            }

            var parser = new EventManifestParser(diags, schemaPath);

            var metadata = parser.ParseWinmeta(winmetaPath);
            if (metadata == null) {
                diags.ReportError("Failed to load winmeta.xml from '{0}'.", winmetaPath);
                return null;
            }

            return metadata;
        }

        private static string FindSchema()
        {
            string sdkPath = WindowsSdkUtils.FindSdkPath();
            return sdkPath != null ? Path.Combine(sdkPath, "eventman.xsd") : null;
        }

        public void AddMetadata(IEventManifestMetadata metadata)
        {
            this.metadata.Add(metadata);
        }

        public EventManifest ParseManifest(Stream input, string inputUri = null)
        {
            using (var reader = CreateXmlReader(input, inputUri))
                return ParseManifest(reader);
        }

        public IEventManifestMetadata ParseWinmeta(Stream input, string inputUri = null)
        {
            IEventManifestSpecification userSpec = manifestSpec;
            try {
                manifestSpec = winmetaSpec;
                return ParseMetadata(input, inputUri);
            } finally {
                manifestSpec = userSpec;
            }
        }

        public IEventManifestMetadata ParseMetadata(Stream input, string inputUri)
        {
            using (var reader = CreateXmlReader(input, inputUri))
                return ParseMetadata(reader);
        }

        private EventManifest ParseManifest(XmlReader reader)
        {
            var trap = diags.TrapError();
            XDocument doc = LoadDocument(reader);
            if (doc == null)
                return null;

            nsResolver = CreateNamespaceResolver(reader);

            XElement manifestElem = doc.XPathSelectElement("e:instrumentationManifest", nsResolver);
            if (manifestElem == null) {
                diags.ReportError(doc.GetLocation(), "Input file contains no instrumentation manifest.");
                return null;
            }

            var manifest = new EventManifest();
            manifest.Location = manifestElem.GetLocation();

            const string ResourcesPath = "e:instrumentationManifest/e:localization/e:resources";
            foreach (XElement elem in doc.XPathSelectElements(ResourcesPath, nsResolver)) {
                var resources = ReadResourceSet(elem);
                if (resources != null)
                    manifest.AddResourceSet(resources);
            }

            var messages = new MessageCollection();

            const string MessagePath = "e:instrumentationManifest/e:instrumentation/e:events/e:messageTable/e:message";
            foreach (XElement elem in doc.XPathSelectElements(MessagePath, nsResolver)) {
                var message = ReadMessage(elem);
                if (message == null || !messages.IsUnique(message, diags))
                    continue;

                foreach (var resourceSet in manifest.Resources) {
                    LocalizedString ls = manifest.GetString(message.MsgRef, resourceSet);
                    if (ls != null) {
                        ls.Id = message.Id;
                        ls.Symbol = message.Symbol;
                    } else if (resourceSet == manifest.PrimaryResourceSet) {
                        diags.ReportError(
                            message.MsgRef.Location,
                            "Message references unknown string '{0}'.",
                            message.MsgRef);
                    } else {
                        diags.Report(
                            DiagnosticSeverity.Warning,
                            message.MsgRef.Location,
                            "String table for culture '{0}' is missing string for message table reference '{1}'.",
                            resourceSet.Culture.Name,
                            message.MsgRef);
                    }
                }

                messages.Add(message);
            }

            const string ProviderPath = "e:instrumentationManifest/e:instrumentation/e:events/e:provider";
            foreach (XElement elem in doc.XPathSelectElements(ProviderPath, nsResolver)) {
                var provider = ReadProvider(elem, manifest);
                manifest.Providers.Add(provider);
            }

            if (trap.ErrorOccurred)
                return null;

            return manifest;
        }

        private IEventManifestMetadata ParseMetadata(XmlReader reader)
        {
            var trap = diags.TrapError();
            var doc = LoadDocument(reader);
            if (doc == null)
                return null;

            nsResolver = CreateNamespaceResolver(reader);

            var meta = new EventManifestMetadata();

            const string StringsPath = "e:instrumentationManifest/e:localization/e:resources[@culture='en-US']/e:stringTable/e:string";
            foreach (XElement elem in doc.XPathSelectElements(StringsPath, nsResolver)) {
                meta.Strings.Add(ReadString(elem));
            }

            const string MetadataPath = "e:instrumentationManifest/e:metadata[@name='evt:meta/winTypes']";
            XElement metaElem = doc.XPathSelectElement(MetadataPath, nsResolver);
            if (metaElem == null) {
                diags.ReportError(
                    new SourceLocation(MetadataPath, 0, 0),
                    "Element instrumentationManifest/metadata not found");
                return null;
            }

            var ctx = new EventManifestMetadataContext(meta);
            foreach (XElement child in metaElem.XPathSelectElements("e:channels/e:channel", nsResolver))
                meta.Channels.TryAdd(ReadChannel(child, ctx), diags);
            foreach (XElement child in metaElem.XPathSelectElements("e:levels/e:level", nsResolver))
                meta.Levels.TryAdd(ReadLevel(child, ctx), diags);
            foreach (XElement child in metaElem.XPathSelectElements("e:tasks/e:task", nsResolver))
                meta.Tasks.TryAdd(ReadTask(child, ctx), diags);
            foreach (XElement child in metaElem.XPathSelectElements("e:opcodes/e:opcode", nsResolver))
                meta.Opcodes.TryAdd(ReadOpcode(child, ctx), diags);
            foreach (XElement child in metaElem.XPathSelectElements("e:keywords/e:keyword", nsResolver))
                meta.Keywords.TryAdd(ReadKeyword(child, ctx), diags);
            foreach (var child in metaElem.XPathSelectElements("e:namedQueries/e:patternMaps/e:patternMap", nsResolver))
                meta.NamedQueries.TryAdd(ReadPatternMap(child, ctx), diags);

            foreach (XElement child in metaElem.XPathSelectElements("e:types/e:xmlTypes/e:xmlType", nsResolver))
                meta.AddXmlType(XmlType.Create(child, nsResolver));

            foreach (XElement child in metaElem.XPathSelectElements("e:types/e:inTypes/e:inType", nsResolver)) {
                var inType = InType.Create(child, nsResolver);
                foreach (XElement outElem in child.XPathSelectElements("e:outType", nsResolver)) {
                    OutType outType = OutType.Create(outElem, nsResolver);
                    XmlType xmlType;
                    if (!meta.TryGetXmlType(outType.XmlType, out xmlType)) {
                        diags.ReportError(
                            outElem.GetLocation(),
                            "InType '{0}' has unknown OutType '{1}'.",
                            inType.Name,
                            outType.XmlType);
                        continue;
                    }

                    if (outType.IsDefault)
                        inType.DefaultOutType = xmlType;
                    inType.OutTypes.Add(xmlType);
                }
                meta.AddInType(inType);
            }

            if (trap.ErrorOccurred)
                return null;

            return meta;
        }

        private XDocument LoadDocument(XmlReader reader)
        {
            failedValidation = false;
            try {
                XDocument doc = XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.SetBaseUri);
                if (failedValidation)
                    return null;
                return doc;
            } catch (XmlException ex) {
                string xmlError = Regex.Replace(ex.Message, @"\. Line \d+, position \d+\.\z", ".");
                var location = new SourceLocation(ex.SourceUri, ex.LineNumber, ex.LinePosition);
                diags.ReportError(location, xmlError);
                return null;
            } finally {
                failedValidation = false;
            }
        }

        private IXmlNamespaceResolver CreateNamespaceResolver(XmlReader reader)
        {
            var nsmgr = new XmlNamespaceManager(reader.NameTable ?? new NameTable());
            nsmgr.AddNamespace("e", EventManifestSchema.Namespace);
            nsmgr.AddNamespace("w", WinEventSchema.Namespace);
            return nsmgr;
        }

        private XmlReader CreateXmlReader(Stream input, string inputUri = null)
        {
            var readerSettings = new XmlReaderSettings();
            if (schemaPath == null) {
                diags.ReportError("Event manifest schema (eventman.xsd) not specified.");
                throw new UserException();
            }

            readerSettings.Schemas.Add(EventManifestSchema.Namespace, schemaPath);
            readerSettings.ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings;
            readerSettings.ValidationType = ValidationType.Schema;
            readerSettings.ValidationEventHandler += OnSchemaValidationEvent;

            return XmlReader.Create(input, readerSettings, inputUri);
        }

        private void OnSchemaValidationEvent(object sender, ValidationEventArgs args)
        {
            if (args.Severity == XmlSeverityType.Error) {
                var location = new SourceLocation(
                    args.Exception.SourceUri,
                    args.Exception.LineNumber,
                    args.Exception.LinePosition);
                diags.ReportError(location, args.Exception.Message);
                failedValidation = true;
            }
        }

        private class MessageEntry
        {
            public MessageEntry(RefValue<string> msgRef, StructValue<uint> id)
            {
                MsgRef = msgRef;
                Id = id;
            }

            public StructValue<uint> Id { get; set; }
            public RefValue<string> MsgRef { get; set; }
            public RefValue<string> Symbol { get; set; }
        }

        private MessageEntry ReadMessage(XElement elem)
        {
            var value = elem.GetString("value");
            var msgRef = elem.GetString("message");
            var symbol = elem.GetOptionalString("symbol");

            uint id;
            if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id))
                return null;

            var msg = new MessageEntry(msgRef, Value.Create(id, value.Location));
            msg.Symbol = symbol;
            return msg;
        }

        private LocalizedResourceSet ReadResourceSet(XElement elem)
        {
            var cultureName = elem.GetString("culture");

            CultureInfo culture;
            try {
                culture = CultureInfo.GetCultureInfo(cultureName);
            } catch (CultureNotFoundException) {
                diags.ReportError(
                    cultureName.Location,
                    "Culture '{0}' is invalid or not supported.",
                    cultureName);
                return null;
            }

            var resources = new LocalizedResourceSet(culture);
            resources.Location = elem.GetLocation();
            foreach (XElement stringElem in elem.XPathSelectElements("e:stringTable/e:string", nsResolver))
                resources.Strings.TryAdd(ReadString(stringElem), diags);

            return resources;
        }

        private LocalizedString ReadString(XElement elem)
        {
            var id = elem.GetString("id");
            var value = elem.GetString("value");

            var ls = new LocalizedString(id, value) {
                Location = elem.GetLocation()
            };

            manifestSpec.IsSatisfiedBy(ls);

            return ls;
        }

        private Provider ReadProvider(XElement elem, EventManifest manifest)
        {
            var name = elem.GetString("name");
            var id = elem.GetGuid("guid");
            var symbol = elem.GetCSymbol("symbol");
            var msgRef = elem.GetOptionalString("message");
            var resourceFileName = elem.GetOptionalString("resourceFileName");
            var messageFileName = elem.GetOptionalString("messageFileName");
            var parameterFileName = elem.GetOptionalString("parameterFileName");

            LocalizedString message = null;
            if (msgRef != null) {
                message = manifest.GetString(msgRef);
                if (message == null) {
                    ReportMissingMessage(msgRef, new ProviderName(name));
                }
            }

            var provider = new Provider(name, id, symbol, message) {
                Location = elem.GetLocation(),
                Manifest = manifest,
                ResourceFileName = resourceFileName,
                MessageFileName = messageFileName,
                ParameterFileName = parameterFileName,
            };

            var ctx = new EventManifestContext(provider, manifest, metadata);

            var importChannelName = EventManifestSchema.Namespace + "importChannel";
            var valueMapName = EventManifestSchema.Namespace + "valueMap";

            foreach (XElement child in elem.XPathSelectElements("e:channels/e:channel | e:channels/e:importChannel", nsResolver)) {
                Channel channel;
                if (child.Name == importChannelName) {
                    channel = ImportChannel(child, ctx);
                    if (channel == null)
                        continue;
                } else
                    channel = ReadChannel(child, ctx);

                channel.Index = provider.Channels.Count;
                if (channel.Value == null)
                    channel.Value = (byte)provider.CreateChannelValue();

                provider.Channels.TryAdd(channel, diags);
            }

            foreach (var child in elem.XPathSelectElements("e:levels/e:level", nsResolver))
                provider.Levels.TryAdd(ReadLevel(child, ctx), diags);
            foreach (var child in elem.XPathSelectElements("e:opcodes/e:opcode", nsResolver))
                provider.Opcodes.TryAdd(ReadOpcode(child, ctx), diags);
            foreach (var child in elem.XPathSelectElements("e:tasks/e:task", nsResolver))
                provider.Tasks.TryAdd(ReadTask(child, ctx), diags);
            foreach (var child in elem.XPathSelectElements("e:keywords/e:keyword", nsResolver))
                provider.Keywords.TryAdd(ReadKeyword(child, ctx), diags);

            foreach (var child in elem.XPathSelectElements("e:maps/e:valueMap | e:maps/e:bitMap", nsResolver)) {
                if (child.Name == valueMapName)
                    provider.Maps.TryAdd(ReadValueMap(child, ctx), diags);
                else
                    provider.Maps.TryAdd(ReadBitMap(child, ctx), diags);
            }

            foreach (var child in elem.XPathSelectElements("e:namedQueries/e:patternMaps/e:patternMap", nsResolver))
                provider.NamedQueries.TryAdd(ReadPatternMap(child, ctx), diags);
            foreach (var child in elem.XPathSelectElements("e:templates/e:template", nsResolver))
                provider.Templates.TryAdd(ReadTemplate(child, ctx), diags);
            foreach (var child in elem.XPathSelectElements("e:filters/e:filter", nsResolver))
                provider.Filters.TryAdd(ReadFilter(child, ctx), diags);
            foreach (var child in elem.XPathSelectElements("e:events/e:event", nsResolver))
                provider.Events.TryAdd(ReadEvent(child, ctx), diags);

            provider.PopulateEnableBits();

            manifestSpec.IsSatisfiedBy(provider);

            return provider;
        }

        private Level ReadLevel(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetQName("name");
            var value = elem.GetUInt8("value");
            var symbol = elem.GetCSymbol("symbol");
            var msgRef = elem.GetOptionalString("message");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null)
                ReportMissingMessage(msgRef, new LevelName(name));

            var level = new Level(name, value, symbol, message) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            manifestSpec.IsSatisfiedBy(level);

            return level;
        }

        private Channel ReadChannel(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetString("name");
            var type = MapChannelType(elem, elem.Attribute("type"));

            var id = elem.GetOptionalString("chid");
            var access = elem.GetOptionalString("access");
            var symbol = elem.GetCSymbol("symbol");
            var value = elem.GetOptionalUInt8("value");
            var isolation = MapIsolationType(elem.Attribute("isolation"));
            var enabled = elem.GetOptionalBool("enabled");
            var msgRef = elem.GetOptionalString("message");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null)
                ReportMissingMessage(msgRef, new ChannelName(name));

            var channel = new Channel(name, type) {
                Id = id,
                Symbol = symbol,
                Value = value,
                Access = access,
                Isolation = isolation,
                Enabled = enabled,
                Message = message,
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            manifestSpec.IsSatisfiedBy(channel);

            return channel;
        }

        private Channel ImportChannel(XElement elem, EventManifestContext ctx)
        {
            var name = elem.GetString("name");
            var id = elem.GetOptionalString("chid");
            var symbol = elem.GetCSymbol("symbol");

            Channel imported = metadata.Coalesce(m => m.Channels.GetByName(name));
            if (imported == null) {
                diags.ReportError(name.Location, "Unable to import unknown channel '{0}'", name);
                diags.Report(
                    DiagnosticSeverity.Note,
                    name.Location,
                    "Known importable channels: {0}",
                    string.Join(", ", metadata.Coalesce(m => m.Channels.Select(c => c.Name))));
                return null;
            }

            var channel = new Channel(imported.Name, imported.Type) {
                Id = id,
                Symbol = symbol,
                Value = imported.Value,
                Message = ctx.ImportString(imported.Message),
                Imported = true,
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            winmetaSpec.IsSatisfiedBy(channel);

            return channel;
        }

        private Task ReadTask(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetQName("name");
            var symbol = elem.GetCSymbol("symbol");
            var value = elem.GetUInt16("value");
            var guid = elem.GetOptionalGuid("eventGUID");
            var msgRef = elem.GetOptionalString("message");

            if (guid != null && guid.Value == Guid.Empty)
                guid = null;

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null)
                ReportMissingMessage(msgRef, new TaskName(name));

            var task = new Task(name, value, symbol, guid, message) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            manifestSpec.IsSatisfiedBy(task);

            return task;
        }

        private Opcode ReadOpcode(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetQName("name");
            var symbol = elem.GetCSymbol("symbol");
            var value = elem.GetUInt8("value");
            var msgRef = elem.GetOptionalString("message");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null)
                ReportMissingMessage(msgRef, new OpcodeName(name));

            var opcode = new Opcode(name, value, symbol, message) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            manifestSpec.IsSatisfiedBy(opcode);

            return opcode;
        }

        private Keyword ReadKeyword(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetQName("name");
            var symbol = elem.GetCSymbol("symbol");
            var mask = elem.GetHexInt64("mask");
            var msgRef = elem.GetOptionalString("message");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null)
                ReportMissingMessage(msgRef, new KeywordName(name));

            var keyword = new Keyword(name, mask, symbol, message) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            manifestSpec.IsSatisfiedBy(keyword);

            return keyword;
        }

        private ValueMap ReadValueMap(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetString("name");
            var symbol = elem.GetCSymbol("symbol");

            var map = new ValueMap(name, symbol) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            foreach (XElement child in elem.XPathSelectElements("e:map", nsResolver)) {
                var data = ReadValueMapItem(child, map, ctx);
                if (data != null)
                    map.Items.TryAdd(data, diags);
            }

            manifestSpec.IsSatisfiedBy(map);

            return map;
        }

        private ValueMapItem ReadValueMapItem(
            XElement elem, ValueMap map, IEventManifestContext ctx)
        {
            var value = elem.GetUInt32("value");
            var msgRef = elem.GetString("message");
            var symbol = elem.GetCSymbol("symbol");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null) {
                ReportMissingMessage(msgRef, new ValueMapName(map.Name));
                return null;
            }

            var item = new ValueMapItem(map, value, symbol, message) {
                Location = elem.GetLocation()
            };

            return item;
        }

        private BitMap ReadBitMap(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetString("name");
            var symbol = elem.GetCSymbol("symbol");

            var map = new BitMap(name, symbol) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            foreach (XElement child in elem.XPathSelectElements("e:map", nsResolver)) {
                var data = ReadBitMapItem(child, map, ctx);
                if (data != null)
                    map.Items.TryAdd(data, diags);
            }

            manifestSpec.IsSatisfiedBy(map);

            return map;
        }

        private BitMapItem ReadBitMapItem(XElement elem, BitMap map, IEventManifestContext ctx)
        {
            var value = elem.GetHexInt32("value");
            var msgRef = elem.GetString("message");
            var symbol = elem.GetCSymbol("symbol");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null) {
                ReportMissingMessage(msgRef, new BitMapName(map.Name));
                return null;
            }

            var item = new BitMapItem(map, value, symbol, message);
            item.Location = elem.GetLocation();

            return item;
        }

        private PatternMap ReadPatternMap(XElement elem, IEventManifestContext ctx)
        {
            var name = elem.GetString("name");
            var format = elem.GetString("format");
            var symbol = elem.GetCSymbol("symbol");

            var map = new PatternMap(name, format, symbol) {
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            foreach (XElement child in elem.XPathSelectElements("e:map", nsResolver)) {
                var item = ReadPatternMapItem(child, map);
                map.Items.TryAdd(item, diags);
            }

            return map;
        }

        private PatternMapItem ReadPatternMapItem(XElement elem, PatternMap map)
        {
            var name = elem.GetString("name");
            var value = elem.GetString("value");

            var item = new PatternMapItem(map, name, value);
            item.Location = elem.GetLocation();

            return item;
        }

        private Template ReadTemplate(XElement elem, EventManifestContext ctx)
        {
            var id = elem.GetString("tid");
            var name = elem.GetOptionalString("name");

            var template = new Template(id) {
                Name = name,
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            var userData = elem.XPathSelectElement("e:UserData", nsResolver);
            if (userData != null) {
                var roots = userData.Elements().ToList();
                if (roots.Count > 1)
                    diags.ReportError(
                        "Template '{0}' has invalid custom XML: '{1}' root elements instead of 1.",
                        template.Name, roots.Count);
                if (roots.Count == 1)
                    template.UserData = ValidateAndCleanUserData(roots[0]);
            }

            var binaryElem = elem.XPathSelectElement("e:binary", nsResolver);
            if (binaryElem != null) {
                diags.ReportError(
                    binaryElem.GetLocation(),
                    "Element provider/template/binary not implemented.");
            }

            XName dataName = EventManifestSchema.Namespace + "data";
            XName structName = EventManifestSchema.Namespace + "struct";

            int propIdx = 0;
            foreach (XElement child in elem.XPathSelectElements("e:data | e:struct", nsResolver)) {
                Property property;
                if (child.Name == dataName)
                    property = ReadDataProperty(child, ctx);
                else if (child.Name == structName)
                    property = ReadStructProperty(child, ctx);
                else
                    continue;

                property.Index = propIdx++;
                template.Properties.TryAdd(property, diags);
            }

            ResolvePropertyRefs(template.Properties);

            manifestSpec.IsSatisfiedBy(template);

            return template;
        }

        private void ResolvePropertyRefs(PropertyCollection properties)
        {
            foreach (var property in properties) {
                ResolvePropertyRef(property, property.Count, properties);
                ResolvePropertyRef(property, property.Length, properties);

                if (property.Kind == PropertyKind.Struct) {
                    var structProperty = (StructProperty)property;
                    ResolvePropertyRefs(structProperty.Properties);
                }
            }
        }

        private void ResolvePropertyRef(
            Property property, IPropertyNumber number, PropertyCollection properties)
        {
            if (number.DataPropertyRef == null)
                return;

            int index = properties.GetIndexByName(number.DataPropertyRef);
            if (index == -1 || !(properties[index] is DataProperty)) {
                diags.ReportError(
                    property.Location,
                    "For property '{0}', property '{1}' referenced by attribute {2} was not found.",
                    property.Name, number.DataPropertyRef, number.Name);
                return;
            }

            number.SetVariable(index, number.DataPropertyRef, (DataProperty)properties[index]);
        }

        private static XElement ValidateAndCleanUserData(XElement userData)
        {
            var elem = new XElement(userData.Name, userData.Attributes());

            bool trailingWhitespace = false;
            foreach (var node in userData.Elements()) {
                elem.Add(new XText("\r\n\t\t"));
                elem.Add(node);
                trailingWhitespace = node.NextNode != null && node.NextNode.NodeType == XmlNodeType.Text;
            }

            if (trailingWhitespace)
                elem.Add(new XText("\r\n\t"));

            return elem;
        }

        private DataProperty ReadDataProperty(XElement elem, EventManifestContext ctx)
        {
            var name = elem.GetString("name");
            InType inType = metadata.Coalesce(m => m.GetInType(elem.GetQName("inType")));
            var outType = elem.GetOptionalQName("outType");
            var mapName = elem.GetOptionalString("map");
            var count = elem.GetOptionalString("count");
            var length = elem.GetOptionalString("length");

            var data = new DataProperty(name, inType);
            data.OutType = outType != null ? metadata.Coalesce(m => m.GetXmlType(outType)) : data.InType.DefaultOutType;
            data.Location = elem.GetLocation();

            SetPropertyNumber(data.Count, count);
            SetPropertyNumber(data.Length, length);

            if (mapName != null) {
                var map = ctx.GetMap(mapName);
                if (map == null)
                    ReportMissingReference(mapName, new DataPropertyName(name), "map", mapName);
                data.Map = map;
            }

            manifestSpec.IsSatisfiedBy(data);

            return data;
        }

        private StructProperty ReadStructProperty(XElement elem, EventManifestContext ctx)
        {
            var name = elem.GetString("name");
            var count = elem.GetOptionalString("count");
            var length = elem.GetOptionalString("length");

            var prop = new StructProperty(name);
            prop.Location = elem.GetLocation();
            SetPropertyNumber(prop.Count, count);
            SetPropertyNumber(prop.Length, length);

            int idx = 0;
            foreach (XElement child in elem.XPathSelectElements("e:data", nsResolver)) {
                var data = ReadDataProperty(child, ctx);
                data.Index = idx++;
                prop.Properties.TryAdd(data, diags);
            }

            manifestSpec.IsSatisfiedBy(prop);

            return prop;
        }

        private Filter ReadFilter(XElement elem, EventManifestContext ctx)
        {
            var name = elem.GetQName("name");
            var symbol = elem.GetCSymbol("symbol");
            var value = elem.GetUInt8("value");
            var version = elem.GetOptionalUInt8("version");
            var templateId = elem.GetOptionalString("tid");
            var msgRef = elem.GetOptionalString("message");

            LocalizedString message = ctx.GetString(msgRef);
            if (msgRef != null && message == null)
                ReportMissingMessage(msgRef, new FilterName(name));

            Template template = ctx.GetTemplate(templateId);
            if (templateId != null && template == null)
                ReportMissingReference(templateId, new FilterName(name), "template", templateId);

            var filter = new Filter(name, value, version.GetValueOrDefault(), symbol, message) {
                Location = elem.GetLocation(),
                Template = template,
                Provider = ctx.Provider
            };

            manifestSpec.IsSatisfiedBy(filter);

            return filter;
        }

        private Event ReadEvent(XElement elem, EventManifestContext ctx)
        {
            var symbol = elem.GetCSymbol("symbol");

            var value = elem.GetUInt32("value");
            var version = elem.GetOptionalUInt8("version");
            var channelToken = elem.GetOptionalString("channel");
            var levelName = elem.GetOptionalQName("level");
            var opcodeName = elem.GetOptionalQName("opcode");
            var taskName = elem.GetOptionalQName("task");
            var keywordList = elem.GetOptionalString("keywords");
            var templateToken = elem.GetOptionalString("template");
            var msgRef = elem.GetOptionalString("message");
            var notLogged = elem.GetOptionalBool("notLogged");

            var evt = new Event(value, version.GetValueOrDefault()) {
                Symbol = symbol,
                NotLogged = notLogged,
                Message = ctx.GetString(msgRef),
                Channel = ctx.GetChannel(channelToken),
                Level = ctx.GetLevel(levelName),
                Opcode = ctx.GetOpcode(opcodeName),
                Task = ctx.GetTask(taskName),
                Template = ctx.GetTemplate(templateToken),
                Location = elem.GetLocation(),
                Provider = ctx.Provider
            };

            if (msgRef != null && evt.Message == null)
                ReportMissingMessage(msgRef, new EventName(value, version, symbol));
            if (channelToken != null && evt.Channel == null)
                ReportMissingReference(channelToken, new EventName(value, version, symbol), "channel", channelToken);
            if (levelName != null && evt.Level == null)
                ReportMissingReference(levelName, new EventName(value, version, symbol), "level", levelName);
            if (opcodeName != null && evt.Opcode == null)
                ReportMissingReference(opcodeName, new EventName(value, version, symbol), "opcode", opcodeName);
            if (taskName != null && evt.Task == null)
                ReportMissingReference(taskName, new EventName(value, version, symbol), "task", taskName);
            if (templateToken != null && evt.Template == null)
                ReportMissingReference(templateToken, new EventName(value, version, symbol), "template", templateToken);

            var xmlns = new XElementNamespaceResolver(elem);
            if (keywordList != null) {
                foreach (var kwNameStr in Regex.Split(keywordList, @" +")) {
                    var keywordName = QName.Parse(kwNameStr, xmlns);
                    var keyword = ctx.GetKeyword(keywordName);
                    if (keyword == null) {
                        ReportMissingReference(keywordList, new EventName(value, version, symbol), "keyword", keywordName);
                        continue;
                    }
                    evt.Keywords.Add(keyword);
                }
            }

            manifestSpec.IsSatisfiedBy(evt);

            return evt;
        }

        private void ReportMissingMessage(RefValue<string> msgRef, EntityName referencing)
        {
            ReportMissingReference(msgRef, referencing, "message", msgRef.Value);
        }

        private void ReportMissingReference<T>(
            RefValue<T> value, EntityName referencing, string referencedType, object referenced)
            where T : class
        {
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} references unknown {1} '{2}'.",
                referencing, referencedType, referenced);

            diags.ReportError(value.Location, message);
        }

        private void SetPropertyNumber(IPropertyNumber number, string value)
        {
            if (value == null)
                return;

            ushort count;
            if (ushort.TryParse(value, out count))
                number.SetFixed(count);
            else
                number.SetVariable(refPropertyName: value);
        }

        private StructValue<ChannelType> MapChannelType(XElement elem, XAttribute attrib)
        {
            if (attrib == null)
                throw CreateSchemaException(elem, "Missing channel type.");

            ChannelType type;
            switch (attrib.Value) {
                case "Admin": type = ChannelType.Admin; break;
                case "Operational": type = ChannelType.Operational; break;
                case "Analytic": type = ChannelType.Analytic; break;
                case "Debug": type = ChannelType.Debug; break;
                default:
                    throw CreateSchemaException(
                        attrib, "Unknown channel type '{0}'.", attrib.Value);
            }

            return Value.CreateStruct(type, attrib.GetLocation());
        }

        private NullableValue<ChannelIsolationType> MapIsolationType(XAttribute attrib)
        {
            if (attrib == null)
                return Value.Create((ChannelIsolationType?)null);

            ChannelIsolationType? type;
            switch (attrib.Value) {
                case "Application": type = ChannelIsolationType.Application; break;
                case "System": type = ChannelIsolationType.System; break;
                case "Custom": type = ChannelIsolationType.Custom; break;
                default:
                    throw CreateSchemaException(
                        attrib, "Unknown channel isolation type '{0}'.", attrib.Value);
            }

            return Value.Create(type, attrib.GetLocation());
        }

        private Exception CreateSchemaException(
            XObject obj, string format, params object[] args)
        {
            string message = string.Format(format, args);
            var location = obj.GetLocation();
            return new SchemaValidationException(
                message,
                location.FilePath,
                location.LineNumber,
                location.ColumnNumber);
        }

        private abstract class EntityName
        {
        }

        private sealed class ProviderName : EntityName
        {
            private readonly string name;

            public ProviderName(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Provider '{0}'", name);
            }
        }

        private sealed class LevelName : EntityName
        {
            private readonly QName name;

            public LevelName(QName name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Level '{0}'", name);
            }
        }

        private sealed class OpcodeName : EntityName
        {
            private readonly QName name;

            public OpcodeName(QName name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Level '{0}'", name);
            }
        }

        private sealed class ValueMapName : EntityName
        {
            private readonly string name;

            public ValueMapName(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "ValueMap '{0}'", name);
            }
        }

        private sealed class BitMapName : EntityName
        {
            private readonly string name;

            public BitMapName(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "BitMap '{0}'", name);
            }
        }

        private sealed class ChannelName : EntityName
        {
            private readonly string name;

            public ChannelName(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Channel '{0}'", name);
            }
        }

        private sealed class TaskName : EntityName
        {
            private readonly QName name;

            public TaskName(QName name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Task '{0}'", name);
            }
        }

        private sealed class KeywordName : EntityName
        {
            private readonly QName name;

            public KeywordName(QName name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Keyword '{0}'", name);
            }
        }

        private sealed class FilterName : EntityName
        {
            private readonly QName name;

            public FilterName(QName name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Filter '{0}'", name);
            }
        }

        private sealed class DataPropertyName : EntityName
        {
            private readonly string name;

            public DataPropertyName(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "Property '{0}'", name);
            }
        }

        private sealed class EventName : EntityName
        {
            private readonly uint value;
            private readonly byte version;
            private readonly string symbol;

            public EventName(uint value, byte? version, string symbol)
            {
                this.value = value;
                this.version = version.GetValueOrDefault();
                this.symbol = symbol;
            }

            public override string ToString()
            {
                string format = symbol != null
                    ? "Event '{0}' with Id:{1}, Version:{2}"
                    : "Event with Id:{1}, Version:{2}";

                return string.Format(
                    CultureInfo.InvariantCulture,
                    format, symbol, value, version);
            }
        }

        private interface IEventManifestContext
        {
            LocalizedString GetString(RefValue<string> msgRef);
            Provider Provider { get; }
        }

        private sealed class EventManifestMetadataContext : IEventManifestContext
        {
            private readonly IEventManifestMetadata metadata;

            public EventManifestMetadataContext(IEventManifestMetadata metadata)
            {
                this.metadata = metadata;
            }

            public LocalizedString GetString(RefValue<string> msgRef)
            {
                if (msgRef == null || metadata == null)
                    return null;

                return metadata.GetString(msgRef);
            }

            public Provider Provider { get { return null; } }
        }

        private sealed class EventManifestContext : IEventManifestContext
        {
            private readonly Provider provider;
            private readonly EventManifest manifest;
            private readonly List<IEventManifestMetadata> metadata;

            public EventManifestContext(
                Provider provider, EventManifest manifest, List<IEventManifestMetadata> metadata)
            {
                this.provider = provider;
                this.manifest = manifest;
                this.metadata = metadata;
            }

            public Provider Provider { get { return provider; } }

            public LocalizedString GetString(RefValue<string> msgRef)
            {
                if (msgRef == null)
                    return null;

                if (manifest == null)
                    return GetString(msgRef);

                LocalizedString ls = manifest.GetString(msgRef);
                if (ls == null) {
                    ls = metadata.Coalesce(m => m.GetString(msgRef));
                    if (ls != null)
                        ls = ImportString(ls);
                }

                return ls;
            }

            public LocalizedString ImportString(LocalizedString message)
            {
                if (message == null)
                    return null;
                return manifest.ImportString(message);
            }

            public Channel GetChannel(string idOrName)
            {
                if (idOrName == null)
                    return null;

                Channel channel = provider.Channels.GetById(idOrName) ??
                                  provider.Channels.GetByName(idOrName);
                if (channel != null)
                    return channel;

                Channel meta = metadata.Coalesce(m =>
                    m.Channels.GetById(idOrName) ??
                    m.Channels.GetByName(idOrName));
                if (meta == null)
                    return null;

                var imported = new Channel(
                    meta.Name,
                    meta.Type,
                    meta.Id,
                    meta.Symbol,
                    meta.Value,
                    meta.Access,
                    meta.Isolation,
                    meta.Enabled,
                    ImportString(meta.Message));
                imported.Provider = provider;
                provider.Channels.Add(imported);

                return imported;
            }

            public Level GetLevel(QName name)
            {
                if (name == null)
                    return null;

                Level level = provider.Levels.GetByName(name);
                if (level != null)
                    return level;

                Level meta = metadata.Coalesce(m => m.Levels.GetByName(name));
                if (meta == null)
                    return null;

                var imported = new Level(
                    meta.Name,
                    meta.Value,
                    null,
                    ImportString(meta.Message));
                imported.Provider = provider;
                imported.Imported = true;
                provider.Levels.Add(imported);

                return imported;
            }

            public Opcode GetOpcode(QName name)
            {
                if (name == null)
                    return null;

                Opcode opcode = provider.Opcodes.GetByName(name);
                if (opcode != null)
                    return opcode;

                Opcode meta = metadata.Coalesce(m => m.Opcodes.GetByName(name));
                if (meta == null)
                    return null;

                var imported = new Opcode(meta.Name, meta.Value, meta.Symbol, meta.Message);
                imported.Provider = provider;
                imported.Imported = true;
                provider.Opcodes.Add(imported);

                return imported;
            }

            public Task GetTask(QName name)
            {
                if (name == null)
                    return null;

                Task task = provider.Tasks.GetByName(name);
                if (task != null)
                    return task;

                Task meta = metadata.Coalesce(m => m.Tasks.GetByName(name));
                if (meta == null)
                    return null;

                var imported = new Task(
                    meta.Name,
                    meta.Value,
                    meta.Symbol,
                    meta.Guid,
                    ImportString(meta.Message));
                imported.Provider = provider;
                imported.Imported = true;
                provider.Tasks.Add(imported);

                return imported;
            }

            public Keyword GetKeyword(QName name)
            {
                if (name == null)
                    return null;

                Keyword keyword = provider.Keywords.GetByName(name);
                if (keyword != null)
                    return keyword;

                Keyword meta = metadata.Coalesce(m => m.Keywords.GetByName(name));
                if (meta == null)
                    return null;

                var imported = new Keyword(
                    meta.Name,
                    meta.Mask,
                    meta.Symbol,
                    ImportString(meta.Message));
                imported.Provider = provider;
                imported.Imported = true;
                provider.Keywords.Add(imported);

                return imported;
            }

            public Template GetTemplate(string id)
            {
                if (id == null)
                    return null;
                return provider.Templates.GetById(id);
            }

            public IMap GetMap(string name)
            {
                return provider.Maps.GetByName(name);
            }
        }

        private sealed class WinMetaManifestSpecification : IEventManifestSpecification
        {
            public bool IsSatisfiedBy(Provider provider)
            {
                return true;
            }

            public bool IsSatisfiedBy(Event @event)
            {
                return true;
            }

            public bool IsSatisfiedBy(Channel channel)
            {
                return true;
            }

            public bool IsSatisfiedBy(Level level)
            {
                return true;
            }

            public bool IsSatisfiedBy(Task task)
            {
                return true;
            }

            public bool IsSatisfiedBy(Opcode opcode)
            {
                return true;
            }

            public bool IsSatisfiedBy(Keyword keyword)
            {
                return true;
            }

            public bool IsSatisfiedBy(Filter filter)
            {
                return true;
            }

            public bool IsSatisfiedBy(Template template)
            {
                return true;
            }

            public bool IsSatisfiedBy(IMap map)
            {
                return true;
            }

            public bool IsSatisfiedBy(DataProperty property)
            {
                return true;
            }

            public bool IsSatisfiedBy(StructProperty property)
            {
                return true;
            }

            public bool IsSatisfiedBy(LocalizedString @string)
            {
                return true;
            }
        }

        private sealed class MessageCollection : ConstrainedEntityCollection<MessageEntry>
        {
            public MessageCollection()
            {
                UniqueConstraintFor(e => e.Id)
                    .WithMessage("Duplicate message id: '{0}'", e => e.Id)
                    .DiagnoseUsing(DiagUtils.ReportError);
                UniqueConstraintFor(e => e.MsgRef)
                    .WithMessage("Duplicate string reference: '{0}'", e => e.MsgRef)
                    .DiagnoseUsing(DiagUtils.ReportError);
                UniqueConstraintFor(e => e.Symbol)
                    .IfNotNull()
                    .WithMessage("Duplicate message symbol: '{0}'", e => e.Symbol)
                    .DiagnoseUsing(DiagUtils.ReportError);
            }
        }
    }
}
