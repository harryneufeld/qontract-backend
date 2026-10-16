using System.Globalization;
using System.Text.RegularExpressions;

namespace Qontract.API.Localization;

/// <summary>Human-reviewed API boundary translations. Domain messages and error
/// codes stay stable for English clients, logs, and existing integrations.</summary>
public static partial class UserMessages
{
    public static bool German => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
    public static string Language => German ? "de" : "en";
    private static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>
    {
        ["Bad Request"] = "Ungültige Anfrage",
        ["Forbidden"] = "Zugriff verweigert",
        ["Not Found"] = "Nicht gefunden",
        ["Internal Server Error"] = "Serverfehler",
        ["Error"] = "Fehler",
        ["Resource not found."] = "Der Eintrag wurde nicht gefunden.",
        ["Access to the requested resource is denied."] = "Du hast keine Berechtigung für diesen Eintrag.",
        ["An unexpected error occurred. Please try again later."] = "Ein unerwarteter Fehler ist aufgetreten. Bitte versuche es später erneut.",
        ["One or more validation errors occurred."] = "Bitte prüfe deine Eingaben.",
        ["Invalid email or password"] = "E-Mail-Adresse oder Passwort ungültig.",
        ["Invalid token subject."] = "Die Sitzung ist ungültig. Bitte melde dich erneut an.",
        ["User no longer active."] = "Das Konto ist nicht mehr aktiv.",
        ["Logged out"] = "Abgemeldet",
        ["Password changed."] = "Passwort geändert.",
        ["Current password is incorrect."] = "Das aktuelle Passwort ist falsch.",
        ["Two-factor authentication is already enabled."] = "Die Zwei-Faktor-Authentifizierung ist bereits aktiviert.",
        ["Account changed. Please sign in again."] = "Das Konto wurde geändert. Bitte melde dich erneut an.",
        ["Restart two-factor setup."] = "Bitte starte die Einrichtung der Zwei-Faktor-Authentifizierung erneut.",
        ["Invalid or already used two-factor code. Try a new code or a recovery code."] = "Ungültiger oder bereits verwendeter Zwei-Faktor-Code. Verwende einen neuen Code oder einen Wiederherstellungscode.",
        ["Too many two-factor attempts. Please try again in five minutes."] = "Zu viele Zwei-Faktor-Fehlversuche. Bitte versuche es in fünf Minuten erneut.",
        ["Two-factor authentication disabled. Please sign in again."] = "Zwei-Faktor-Authentifizierung deaktiviert. Bitte melde dich erneut an.",
        ["Account unavailable."] = "Das Konto ist nicht verfügbar.",
        ["Two-factor secret protection is unavailable."] = "Die Zwei-Faktor-Authentifizierung ist derzeit nicht verfügbar.",
        ["Session is no longer active."] = "Die Sitzung ist nicht mehr aktiv.",
        ["Session expired."] = "Die Sitzung ist abgelaufen.",
        ["Invalid tenant type."] = "Die Art der Organisation ist ungültig.",
        ["Invalid role."] = "Die Rolle ist ungültig.",
        ["Invalid entity type."] = "Die Art des Eintrags ist ungültig.",
        ["Invalid email format."] = "Die E-Mail-Adresse ist ungültig.",
        ["Type must be Individual or Company."] = "Die Art muss Privatperson oder Unternehmen sein.",
        ["Status must be Draft, Active, Expired or Terminated."] = "Der Status muss Entwurf, Aktiv, Abgelaufen oder Gekündigt sein.",
        ["Do not specify Type together with ClearType."] = "Wähle eine Vertragsart oder entferne die Zuordnung, nicht beides.",
        ["Use a valid yyyy-MM-dd price effective date."] = "Gib ein gültiges Datum für den Preisbeginn an.",
        ["Choose a responsible member or clear the assignment, not both."] = "Wähle ein zuständiges Mitglied oder entferne die Zuordnung, nicht beides.",
        ["Responsible user must be an active member of this workspace."] = "Das zuständige Mitglied muss in diesem Arbeitsbereich aktiv sein.",
        ["The restoration period has expired."] = "Die Frist zur Wiederherstellung ist abgelaufen.",
        ["Contract retention must be between 1 and 3650 days."] = "Die Aufbewahrungsfrist muss zwischen 1 und 3650 Tagen liegen.",
        ["Tenant settings must be valid JSON."] = "Die Organisationseinstellungen sind ungültig.",
        ["Enter a three-letter currency code."] = "Gib einen dreistelligen Währungscode ein.",
        ["Default currency must be a three-letter currency code."] = "Die Standardwährung muss ein dreistelliger Währungscode sein.",
        ["Enter at least one address field."] = "Gib mindestens ein Adressfeld an.",
        ["ContractorId is required for contractor addresses."] = "Für diese Adresse ist ein Vertragspartner erforderlich.",
        ["ContractId is required for contract addresses."] = "Für diese Adresse ist ein Vertrag erforderlich.",
        ["Price effective date must be a valid date on or after the current price starts."] = "Das Preisdatum muss gültig sein und am oder nach dem Beginn des aktuellen Preises liegen.",
        ["Billed amount must be a non-negative number without currency symbols or grouping separators."] = "Der Abrechnungsbetrag muss eine nicht negative Zahl ohne Währung oder Tausendertrennzeichen sein.",
        ["Unknown document scope."] = "Unbekannter Dateibereich.",
        ["An authenticated owner is required."] = "Ein angemeldetes Konto ist erforderlich.",
        ["A valid document workspace is required."] = "Ein gültiger Arbeitsbereich für die Datei ist erforderlich.",
        ["File name and type are required."] = "Dateiname und Dateityp sind erforderlich.",
        ["File data is required."] = "Dateidaten sind erforderlich.",
        ["File data is not valid base64."] = "Die Dateidaten sind ungültig.",
        ["File name must not be empty."] = "Der Dateiname darf nicht leer sein.",
        ["This document is evidence for an active invoice. Remove that reference or archive the invoice first."] = "Diese Datei ist ein Beleg für eine aktive Rechnung. Entferne zuerst die Belegverknüpfung oder archiviere die Rechnung.",
        ["Personal files cannot be linked to shared contracts."] = "Persönliche Dateien können nicht mit gemeinsamen Verträgen verknüpft werden.",
        ["The original contract is the storage anchor and cannot be unlinked. You can move the document to trash instead."] = "Der ursprüngliche Vertrag ist der Speicherort dieser Datei. Diese Verknüpfung kann nicht entfernt werden. Du kannst die Datei in den Papierkorb verschieben.",
        ["This link is used by an active invoice. Remove that invoice reference first."] = "Diese Verknüpfung wird von einer aktiven Rechnung verwendet. Entferne zuerst die Belegverknüpfung der Rechnung.",
        ["Invalid scope or pagination."] = "Arbeitsbereich oder Seitenangabe ungültig.",
        ["Document metadata is empty or exceeds its permitted length."] = "Die Dateiangaben sind leer oder überschreiten die zulässige Länge.",
        ["Invalid page size or page."] = "Seite oder Seitengröße ungültig.",
        ["Restore the invoice before editing it."] = "Stelle die Rechnung vor dem Bearbeiten wieder her.",
        ["Amount must be a decimal without grouping or currency symbols."] = "Der Betrag muss eine Zahl ohne Währung oder Tausendertrennzeichen sein.",
        ["Currency must be a three-letter code or left unknown."] = "Die Währung muss ein dreistelliger Code sein oder leer bleiben.",
        ["Invoice number or description is too long."] = "Rechnungsnummer oder Beschreibung ist zu lang.",
        ["Restore the invoice document first."] = "Stelle zuerst die Belegdatei der Rechnung wieder her.",
        ["Dates must use yyyy-MM-dd."] = "Das übermittelte Datum ist ungültig.",
        ["Invitation has already been used."] = "Diese Einladung wurde bereits verwendet.",
        ["Invitation is invalid, expired, or unavailable for this account."] = "Die Einladung ist ungültig, abgelaufen oder für dieses Konto nicht verfügbar.",
        ["Public registration is disabled."] = "Die öffentliche Registrierung ist deaktiviert.",
        ["An account already exists. Sign in to accept an invitation."] = "Ein Konto ist bereits vorhanden. Melde dich an, um die Einladung anzunehmen.",
        ["Invitation is no longer available."] = "Diese Einladung ist nicht mehr verfügbar.",
        ["You already belong to this tenant."] = "Du bist bereits Mitglied dieser Organisation.",
        ["An authenticated workspace owner is required."] = "Ein angemeldetes Konto für den Arbeitsbereich ist erforderlich.",
        ["An account already exists. Use an invitation to add an existing user."] = "Ein Konto ist bereits vorhanden. Füge bestehende Konten über eine Einladung hinzu.",
        ["The tenant must retain an active administrator."] = "Die Organisation muss mindestens einen aktiven Administrator behalten.",
        ["Shared account details must be changed by the account owner."] = "Gemeinsame Kontodaten können nur vom Kontoinhaber geändert werden.",
        ["This email is already in use."] = "Diese E-Mail-Adresse wird bereits verwendet.",
        ["Only tenant administrators can change tenant addresses."] = "Nur Administratoren der Organisation können ihre Adressen ändern.",
        ["Password reset is not available. Please contact the administrator."] = "Das Zurücksetzen des Passworts ist nicht verfügbar. Bitte wende dich an den Administrator.",
        ["Please try again later."] = "Bitte versuche es später erneut.",
        ["If this account is eligible, a password reset link will be sent."] = "Falls dieses Konto berechtigt ist, wird ein Link zum Zurücksetzen des Passworts versendet.",
        ["This link is invalid or expired. Please request a new link."] = "Dieser Link ist ungültig oder abgelaufen. Bitte fordere einen neuen Link an.",
        ["Unable to complete reset. Please retry or request a new link."] = "Das Zurücksetzen konnte nicht abgeschlossen werden. Bitte versuche es erneut oder fordere einen neuen Link an.",
        ["Password updated. Please sign in again."] = "Passwort aktualisiert. Bitte melde dich erneut an."
    };
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "Name", ["Tenant name"] = "Name der Organisation", ["Email"] = "E-Mail-Adresse",
        ["Password"] = "Passwort", ["Current password"] = "Aktuelles Passwort", ["New password"] = "Neues Passwort",
        ["Title"] = "Titel", ["Description"] = "Beschreibung", ["Settings"] = "Einstellungen",
        ["Phone"] = "Telefonnummer", ["Currency"] = "Währung", ["Value"] = "Betrag",
        ["ContractNumber"] = "Vertragsnummer", ["MinimumContractDate"] = "Frühestes Vertragsende",
        ["StartDate"] = "Startdatum", ["EndDate"] = "Enddatum",
        ["FileName"] = "Dateiname", ["FileType"] = "Dateityp", ["FileData"] = "Dateidaten",
        ["ContractId"] = "Vertrag", ["Street"] = "Straße", ["House number"] = "Hausnummer",
        ["City"] = "Ort", ["Zip"] = "Postleitzahl", ["Country"] = "Land",
        ["Token"] = "Link", ["NewPassword"] = "Neues Passwort", ["Type"] = "Art", ["BillingPeriod"] = "Abrechnungszeitraum"
    };

    public static string Translate(string message)
    {
        if (!German) return message;
        if (Messages.TryGetValue(message, out var result)) return result;
        var required = Regex.Match(message, @"^(.*?) is required\.$");
        if (required.Success && Fields.TryGetValue(required.Groups[1].Value, out var field)) return $"{field} ist erforderlich.";
        var length = Regex.Match(message, @"^(.*?) cannot exceed (\d+) characters\.$");
        if (length.Success && Fields.TryGetValue(length.Groups[1].Value, out field)) return $"{field} darf höchstens {length.Groups[2].Value} Zeichen lang sein.";
        var minimum = Regex.Match(message, @"^(.*?) must be at least (\d+) characters\.$");
        if (minimum.Success && Fields.TryGetValue(minimum.Groups[1].Value, out field)) return $"{field} muss mindestens {minimum.Groups[2].Value} Zeichen lang sein.";
        // Framework/model-binding and new errors get a safe localized fallback;
        // their stable field names and error codes remain available to clients.
        return "Bitte prüfe deine Eingaben und versuche es erneut.";
    }
}
