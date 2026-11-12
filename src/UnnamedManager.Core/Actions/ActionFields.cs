using System.Text.Json;

namespace UnnamedManager.Core.Config;

/// One action's fields as a family reads them, with the same checks and
/// messages for every family. Every problem names the action id, the subject
/// the fixtures expect.
public sealed class ActionFields
{
    private readonly JsonElement _object;
    private readonly Func<JsonElement, string, ConfigAction> _readAction;

    internal ActionFields(JsonElement obj, string id, string type, Func<JsonElement, string, ConfigAction> readAction)
    {
        _object = obj;
        Id = id;
        Type = type;
        _readAction = readAction;
    }

    /// The action id, or the id of the Context Aware action it sits in.
    public string Id { get; }
    public string Type { get; }

    /// "action “open-editor”", for messages.
    public string Place => $"action “{Id}”";

    public ConfigProblem Problem(string rule, string message) => new(rule, message, Id);

    /// Reports the first field that isn't `type` or one of `names`. Call it
    /// first, so a typo is reported before anything else.
    public void Allow(params string[] names)
    {
        foreach (var property in _object.EnumerateObject())
        {
            if (property.Name != "type" && !names.Contains(property.Name))
                throw Problem(Rule.UnknownField, $"Unknown field “{property.Name}” in {Place}. Check its spelling, or remove it.");
        }
    }

    public bool Has(string name) => _object.TryGetProperty(name, out _);

    private JsonElement? Get(string name) => _object.TryGetProperty(name, out var value) ? value : null;

    private ConfigProblem WrongType(string name, string kind) =>
        Problem(Rule.WrongType, $"“{name}” in {Place} should be {kind}.");

    public string RequiredString(string name) =>
        OptionalString(name) ?? throw Problem(Rule.MissingField, $"Missing “{name}” in {Place}.");

    public string? OptionalString(string name) => Get(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } v => v.GetString()!,
        _ => throw WrongType(name, "text in quotes"),
    };

    public bool Bool(string name, bool fallback) => Get(name) switch
    {
        null => fallback,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => throw WrongType(name, "true or false"),
    };

    public IReadOnlyList<string>? Strings(string name)
    {
        if (Get(name) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.String))
            throw WrongType(name, "a list of text");
        return value.EnumerateArray().Select(i => i.GetString()!).ToList();
    }

    public IReadOnlyList<KeyValuePair<string, string>>? StringMap(string name)
    {
        if (Get(name) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String))
            throw WrongType(name, "an object of text");
        return value.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value.GetString()!)).ToList();
    }

    public IReadOnlyList<double>? Numbers(string name)
    {
        if (Get(name) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.Number))
            throw WrongType(name, "a list of numbers");
        return value.EnumerateArray().Select(i => i.GetDouble()).ToList();
    }

    /// An object of numbers, as strict as an action: unknown keys and missing
    /// required ones are reported.
    public IReadOnlyDictionary<string, double>? NumberObject(string name, string[] required, string[]? others = null)
    {
        if (Get(name) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Object) throw WrongType(name, "an object of numbers");
        var result = new Dictionary<string, double>();
        foreach (var property in value.EnumerateObject())
        {
            if (!required.Contains(property.Name) && !(others ?? []).Contains(property.Name))
                throw Problem(Rule.UnknownField, $"Unknown field “{property.Name}” in “{name}” in {Place}. Check its spelling, or remove it.");
            if (property.Value.ValueKind != JsonValueKind.Number)
                throw Problem(Rule.WrongType, $"“{property.Name}” in “{name}” in {Place} should be a number.");
            result[property.Name] = property.Value.GetDouble();
        }
        if (required.FirstOrDefault(r => !result.ContainsKey(r)) is { } missing)
            throw Problem(Rule.MissingField, $"Missing “{missing}” in “{name}” in {Place}.");
        return result;
    }

    /// Which one of `names` the action has. Two is `conflicting-fields`, none is `missing-field`.
    public string OneOf(params string[] names)
    {
        var present = names.Where(Has).ToList();
        if (present.Count > 1)
            throw Problem(Rule.ConflictingFields, $"Use only one of “{present[0]}” and “{present[1]}” in {Place}.");
        if (present.Count == 0)
            throw Problem(Rule.MissingField, $"Add {Join(names.Select(n => $"“{n}”"))} to {Place}.");
        return present[0];
    }

    public ConfigProblem Conflict(string name, string other) =>
        Problem(Rule.ConflictingFields, $"“{name}” doesn't go with “{other}” in {Place}.");

    /// One of a field's values, such as a preset or a direction, under `rule`.
    public string? Choice(string name, IReadOnlyList<string> values, string rule)
    {
        if (OptionalString(name) is not { } raw) return null;
        if (!values.Contains(raw))
            throw Problem(rule, $"Unknown {name} “{raw}” in {Place}. Use {Join(values)}.");
        return raw;
    }

    /// A nested action (any family, or Context Aware), read the same way.
    public ConfigAction? Action(string name) => Get(name) is { } value ? _readAction(value, Id) : null;

    /// The nested actions of an object such as `variants`, in file order.
    public IReadOnlyList<KeyValuePair<string, ConfigAction>> ActionMap(string name)
    {
        if (Get(name) is not { } value) return [];
        if (value.ValueKind != JsonValueKind.Object) throw WrongType(name, "an object");
        return value.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, _readAction(p.Value, Id))).ToList();
    }

    /// "a, b or c".
    public static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Join("", list) : string.Join(", ", list[..^1]) + " or " + list[^1];
    }
}
