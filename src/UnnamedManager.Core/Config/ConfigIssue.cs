namespace UnnamedManager.Core.Config;

/// The rule ids in the shared spec (spec/CONFIG-SPEC.md, Validation), so the
/// Mac and Windows apps are checked against the same fixtures. Never shown in the UI.
public static class Rule
{
    // Errors
    public const string InvalidJson = "invalid-json";
    public const string Version = "version";
    public const string UnknownField = "unknown-field";
    public const string MissingField = "missing-field";
    public const string WrongType = "wrong-type";
    public const string NoEntries = "no-entries";
    public const string EmptyTitle = "empty-title";
    public const string BadKeys = "bad-keys";
    public const string DuplicatePath = "duplicate-path";
    public const string NoParent = "no-parent";
    public const string ParentIsBinding = "parent-is-binding";
    public const string EmptyMenu = "empty-menu";
    public const string StepModifiers = "step-modifiers";
    public const string StepIsRuntimeKey = "step-is-runtime-key";
    public const string BadRuntimeKey = "bad-runtime-key";
    public const string SameCancelBack = "same-cancel-back";
    public const string NegativeDelay = "negative-delay";
    public const string NegativeGap = "negative-gap";
    public const string BadWindowLimit = "bad-window-limit";
    public const string NegativeWindowDistance = "negative-window-distance";
    public const string BadUIValue = "bad-ui-value";
    public const string ReservedChord = "reserved-chord";
    public const string UndefinedAction = "undefined-action";
    public const string UnknownActionType = "unknown-action-type";
    public const string BadAppMode = "bad-app-mode";
    public const string UndefinedContext = "undefined-context";
    public const string NestedContextual = "nested-contextual";
    public const string DuplicateContext = "duplicate-context";
    public const string ConflictingFields = "conflicting-fields";
    public const string BadFraction = "bad-fraction";
    public const string BadWindowValue = "bad-window-value";
    public const string UnsupportedAction = "unsupported-action";
    public const string OtherPlatformField = "other-platform-field";
    public const string BadAppId = "bad-app-id";
    public const string BadIcon = "bad-icon";
    // Warnings
    public const string UnusedAction = "unused-action";
    public const string UnusedContext = "unused-context";
    public const string KeyNotOnPlatform = "key-not-on-platform";
    public const string OfficeShortcut = "office-shortcut";
}

public enum Severity { Error, Warning }

/// One problem in the config file, as a sentence for the tray menu and the
/// Bindings window. <see cref="Subject"/> is the entry path, action id or
/// context id it's about; <see cref="Rule"/> is the spec rule id, or null for
/// a problem outside the file's contents (a file that can't be read).
public sealed record ConfigIssue(Severity Severity, string Message, string? Subject = null, string? Rule = null)
{
    public override string ToString() => Message;
}

/// Thrown while reading the file: the first problem stops the read, as in the
/// Mac app (spec, Fixtures: "a file that fails to decode stops at its first problem").
public sealed class ConfigProblem(string rule, string message, string? subject = null) : Exception(message)
{
    public string Rule { get; } = rule;
    public string? Subject { get; } = subject;

    public ConfigIssue ToIssue() => new(Severity.Error, Message, Subject, Rule);
}
