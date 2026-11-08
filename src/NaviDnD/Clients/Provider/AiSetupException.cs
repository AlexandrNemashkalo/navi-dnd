namespace NaviDnD.Clients;

// The AI cannot be launched due to the settings (CLI is not installed, there is no input): “repeat the action”
// will not help here - the player is shown the error text itself, even without debugging
// (GameAiClient.RecordError).
public class AiSetupException(string message) : Exception(message);
