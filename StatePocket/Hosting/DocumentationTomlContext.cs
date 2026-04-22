using Tomlyn.Serialization;

namespace StatePocket.Hosting;

[TomlSerializable(typeof(DocumentationFrontMatter))]
internal sealed partial class DocumentationTomlContext : TomlSerializerContext;
