namespace Saham.Integrations.Push;

/* Configuration section "Firebase": the service account of the Firebase project (Project settings →
 * Service accounts → Generate new private key). Either the path of that JSON file or its content
 * (handy as a hosting environment variable). Keep it in appsettings.secrets.json, never in git.
 * Empty = pushes are only written to the log (LoggingPushSender). */
public class FirebaseOptions
{
    public string? CredentialPath { get; set; }

    public string? CredentialJson { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CredentialPath) || !string.IsNullOrWhiteSpace(CredentialJson);
}
