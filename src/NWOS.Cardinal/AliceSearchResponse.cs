namespace NWOS.Cardinal;

internal sealed record AliceSearchResponse(string Message, IReadOnlyList<AliceSuggestedAction> Actions);

internal sealed record AliceSuggestedAction(
    string Label,
    string Target,
    string Action,
    string Payload,
    string LaunchPath);
