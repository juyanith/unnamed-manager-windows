using System.Text.Json.Nodes;

namespace UnnamedManager.Core.Config;

/// One `type` a family handles: its name in the file and its glossary name.
public sealed record ActionKind(string Type, string DisplayName);

/// An action family: one module that reads its own kinds of action. Reading
/// comes first (S-002); each family's story adds writing, its schema and its
/// plan (S-006 onward). The engine and the Guide know no type by name.
public interface IActionFamily
{
    IReadOnlyList<ActionKind> Kinds { get; }

    ConfigAction Read(ActionFields fields);

    /// The fields after `type`, in the order they're written, defaults left out.
    /// Empty until the family's story builds its writer.
    IReadOnlyList<KeyValuePair<string, JsonNode>> Write(ConfigAction action) => [];

    /// Its variants in the JSON Schema's `action` list, `type` included. Empty until built.
    IReadOnlyList<JsonObject> Schema(string type) => [];

    /// What to do in the world, or null when the family's story hasn't built it yet.
    ActionPlan? Plan(ConfigAction action) => null;
}

/// The families the app knows. The reader, and later the writer, the schema and
/// the Actions page, read it, so adding a family is one line here.
public sealed class ActionRegistry(IReadOnlyList<IActionFamily> families)
{
    public static ActionRegistry Standard { get; } = new([
        new UseApplicationFamily(), new OpenFamily(), new RunCommandFamily(), new SendShortcutFamily(), new BasicFamily(),
        new MaximizeFamily(), new MinimizeFamily(), new PlaceFamily(), new MoveFamily(), new ResizeFamily(), new FocusFamily(),
    ]);

    public IReadOnlyList<IActionFamily> Families { get; } = families;

    public IEnumerable<ActionKind> Kinds => Families.SelectMany(f => f.Kinds);

    public IActionFamily? Family(string type) => Families.FirstOrDefault(f => f.Kinds.Any(k => k.Type == type));
}
