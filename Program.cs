// GContacts - finds Google Contacts that have neither a phone number nor an email, prints all available metadata for each, then permanently deletes them via the Google People API.
//
// One-time setup (see README.md for details):
//   1. In Google Cloud Console, create a project and enable the "People API".
//   2. Configure an OAuth consent screen and create an OAuth client ID of type "Desktop app".
//   3. Download the credentials JSON and save it next to this file as "client_secret.json".
//   4. Run with:  dotnet run
//      A browser window opens the first time so you can grant access to read and manage your contacts.

using Google.Apis.Auth.OAuth2;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace GContacts;

internal static class Program
{
    // Full read/write access so the app can delete contacts (create/update not implemented).
    private static readonly string[] Scopes = { PeopleServiceService.Scope.Contacts };
    private const string ApplicationName = "GContacts";
    private const string CredentialsFile = "client_secret.json";

    // Every field the People API can return for a connection, so the app can print all available metadata for each person.
    private const string AllPersonFields = "addresses,ageRanges,biographies,birthdays,calendarUrls,clientData,coverPhotos,emailAddresses,events,externalIds,genders,imClients,interests,locales,locations,memberships,metadata,miscKeywords,names,nicknames,occupations,organizations,phoneNumbers,photos,relations,sipAddresses,skills,urls,userDefined";

    // On a quota/rate-limit error the app waits this long, then retries the same contact once.
    private static readonly TimeSpan QuotaRetryDelay = TimeSpan.FromMinutes(5);

    private static async Task<int> Main()
    {
        try
        {
            if (!File.Exists(CredentialsFile))
            {
                Console.Error.WriteLine($"Missing '{CredentialsFile}'. See README.md for how to create OAuth credentials in Google Cloud Console.");
                return 1;
            }

            UserCredential credential = await AuthorizeAsync();
            using PeopleServiceService service = new PeopleServiceService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = ApplicationName });

            List<Person> contacts = await GetAllContactsAsync(service);
            Console.WriteLine();
            Console.WriteLine($"Retrieved {contacts.Count} contact(s) from your Google account.");
            Console.WriteLine();

            await DisplayAndDeleteContactsAsync(service, contacts);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<UserCredential> AuthorizeAsync()
    {
        // Cache OAuth tokens under %AppData%\GContacts\token so sign-in only happens once.
        string tokenFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName, "token");
        await using FileStream stream = new FileStream(CredentialsFile, FileMode.Open, FileAccess.Read);
        ClientSecrets secrets = (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets;
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(secrets, Scopes, "user", CancellationToken.None, new FileDataStore(tokenFolder, true));
    }

    private static async Task<List<Person>> GetAllContactsAsync(PeopleServiceService service)
    {
        List<Person> people = new List<Person>();
        string? pageToken = null;
        do
        {
            PeopleResource.ConnectionsResource.ListRequest request = service.People.Connections.List("people/me");
            request.PersonFields = AllPersonFields;
            request.PageSize = 1000;
            request.SortOrder = PeopleResource.ConnectionsResource.ListRequest.SortOrderEnum.FIRSTNAMEASCENDING;
            request.PageToken = pageToken;

            ListConnectionsResponse response = await request.ExecuteAsync();
            if (response.Connections != null) { people.AddRange(response.Connections); }
            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return people;
    }

    private static async Task DisplayAndDeleteContactsAsync(PeopleServiceService service, IReadOnlyList<Person> contacts)
    {
        List<Person> toDelete = contacts.Where(person => !HasPhoneNumber(person) && !HasEmail(person)).ToList();
        if (toDelete.Count == 0)
        {
            Console.WriteLine("No contacts without both a phone number and an email were found.");
            return;
        }

        Console.WriteLine($"{toDelete.Count} of {contacts.Count} contact(s) have no phone number and no email.");
        Console.WriteLine("Each will be displayed and then PERMANENTLY deleted. This cannot be undone.");
        Console.Write("Type 'yes' to continue: ");
        string? answer = Console.ReadLine();
        if (!string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Aborted. No contacts were deleted.");
            return;
        }

        Console.WriteLine();
        int index = 1;
        int deleted = 0;
        foreach (Person person in toDelete)
        {
            DisplayPerson(index, person);
            if (await DeleteContactAsync(service, person)) { deleted++; }
            Console.WriteLine();
            index++;
        }

        Console.WriteLine($"Deleted {deleted} of {toDelete.Count} contact(s).");
    }

    private static async Task<bool> DeleteContactAsync(PeopleServiceService service, Person person)
    {
        string? resourceName = person.ResourceName;
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            Console.Error.WriteLine("    Skipped deletion: contact has no resource name.");
            return false;
        }

        try
        {
            await service.People.DeleteContact(resourceName).ExecuteAsync();
            Console.WriteLine("    Deleted.");
            return true;
        }
        catch (Exception ex) when (IsQuotaExceeded(ex))
        {
            // Quota exceeded: wait, then retry this same contact once.
            Console.Error.WriteLine($"    Quota exceeded for '{resourceName}'. Waiting {QuotaRetryDelay.TotalMinutes:0} minutes before one retry...");
            await Task.Delay(QuotaRetryDelay);

            try
            {
                await service.People.DeleteContact(resourceName).ExecuteAsync();
                Console.WriteLine("    Deleted (after retry).");
                return true;
            }
            catch (Exception retryEx) when (IsQuotaExceeded(retryEx))
            {
                // A second consecutive quota error for the same contact terminates the run.
                throw new InvalidOperationException($"Quota exceeded again for '{resourceName}' after waiting {QuotaRetryDelay.TotalMinutes:0} minutes. Terminating.", retryEx);
            }
        }
    }

    private static bool IsQuotaExceeded(Exception ex)
    {
        if (ex is not Google.GoogleApiException apiEx) { return false; }
        if (apiEx.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests) { return true; }

        Google.Apis.Requests.RequestError? error = apiEx.Error;
        if (error?.Errors == null) { return false; }
        foreach (Google.Apis.Requests.SingleError single in error.Errors)
        {
            string reason = single.Reason ?? string.Empty;
            if (reason.Contains("quota", StringComparison.OrdinalIgnoreCase) || reason.Contains("rateLimit", StringComparison.OrdinalIgnoreCase)) { return true; }
        }

        return false;
    }

    private static bool HasPhoneNumber(Person person)
    {
        return person.PhoneNumbers != null && person.PhoneNumbers.Any(phone => !string.IsNullOrWhiteSpace(phone.Value));
    }

    private static bool HasEmail(Person person)
    {
        return person.EmailAddresses != null && person.EmailAddresses.Any(email => !string.IsNullOrWhiteSpace(email.Value));
    }

    private static void DisplayPerson(int index, Person person)
    {
        string name = person.Names?.FirstOrDefault()?.DisplayName ?? "(no name)";
        Console.WriteLine($"[{index}] {name}");

        PrintField("Resource name", person.ResourceName);
        PrintValues("Names", person.Names?.Select(FormatName));
        PrintValues("Nicknames", person.Nicknames?.Select(n => n.Value));
        PrintValues("Email addresses", person.EmailAddresses?.Select(e => WithType(e.Value, e.FormattedType ?? e.Type)));
        PrintValues("Phone numbers", person.PhoneNumbers?.Select(p => WithType(p.Value, p.FormattedType ?? p.Type)));
        PrintValues("Addresses", person.Addresses?.Select(a => WithType(a.FormattedValue, a.FormattedType ?? a.Type)));
        PrintValues("Organizations", person.Organizations?.Select(FormatOrganization));
        PrintValues("Occupations", person.Occupations?.Select(o => o.Value));
        PrintValues("Birthdays", person.Birthdays?.Select(FormatBirthday));
        PrintValues("Events", person.Events?.Select(ev => WithType(FormatDate(ev.Date), ev.FormattedType ?? ev.Type)));
        PrintValues("Genders", person.Genders?.Select(g => g.FormattedValue ?? g.Value));
        PrintValues("Biographies", person.Biographies?.Select(b => b.Value));
        PrintValues("URLs", person.Urls?.Select(u => WithType(u.Value, u.FormattedType ?? u.Type)));
        PrintValues("IM clients", person.ImClients?.Select(c => WithType(c.Username, c.FormattedProtocol ?? c.Protocol)));
        PrintValues("SIP addresses", person.SipAddresses?.Select(s => WithType(s.Value, s.FormattedType ?? s.Type)));
        PrintValues("Relations", person.Relations?.Select(r => WithType(r.Person, r.FormattedType ?? r.Type)));
        PrintValues("Interests", person.Interests?.Select(x => x.Value));
        PrintValues("Skills", person.Skills?.Select(x => x.Value));
        PrintValues("Locales", person.Locales?.Select(x => x.Value));
        PrintValues("Locations", person.Locations?.Select(x => x.Value));
        PrintValues("Misc keywords", person.MiscKeywords?.Select(k => WithType(k.Value, k.FormattedType ?? k.Type)));
        PrintValues("External IDs", person.ExternalIds?.Select(x => WithType(x.Value, x.FormattedType ?? x.Type)));
        PrintValues("Calendar URLs", person.CalendarUrls?.Select(c => WithType(c.Url, c.FormattedType ?? c.Type)));
        PrintValues("Age ranges", person.AgeRanges?.Select(a => a.AgeRange));
        PrintValues("Memberships", person.Memberships?.Select(FormatMembership));
        PrintValues("User-defined", person.UserDefined?.Select(u => $"{u.Key}: {u.Value}"));
        PrintValues("Client data", person.ClientData?.Select(c => $"{c.Key}: {c.Value}"));
        PrintValues("Photos", person.Photos?.Select(p => p.Url));
        PrintValues("Cover photos", person.CoverPhotos?.Select(p => p.Url));
        PrintValues("Sources", person.Metadata?.Sources?.Select(FormatSource));

        DateTimeOffset? modified = GetModifiedTime(person);
        if (modified.HasValue) { PrintField("Last modified", modified.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")); }
    }

    private static string? FormatName(Name name)
    {
        List<string> parts = new List<string>();
        AddPart(parts, "display", name.DisplayName);
        AddPart(parts, "prefix", name.HonorificPrefix);
        AddPart(parts, "given", name.GivenName);
        AddPart(parts, "middle", name.MiddleName);
        AddPart(parts, "family", name.FamilyName);
        AddPart(parts, "suffix", name.HonorificSuffix);
        AddPart(parts, "phonetic", name.PhoneticFullName);
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static void AddPart(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) { parts.Add($"{label}={value}"); }
    }

    private static string? FormatOrganization(Organization org)
    {
        List<string> parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(org.Title)) { parts.Add(org.Title); }
        if (!string.IsNullOrWhiteSpace(org.Name)) { parts.Add($"at {org.Name}"); }
        if (!string.IsNullOrWhiteSpace(org.Department)) { parts.Add($"({org.Department})"); }
        return WithType(string.Join(" ", parts), org.FormattedType ?? org.Type);
    }

    private static string? FormatBirthday(Birthday birthday)
    {
        string? date = FormatDate(birthday.Date);
        return !string.IsNullOrWhiteSpace(date) ? date : birthday.Text;
    }

    private static string? FormatDate(Date? date)
    {
        if (date == null) { return null; }
        if (date.Year.HasValue) { return $"{date.Year.Value:D4}-{date.Month ?? 0:D2}-{date.Day ?? 0:D2}"; }
        if (date.Month.HasValue || date.Day.HasValue) { return $"{date.Month ?? 0:D2}-{date.Day ?? 0:D2}"; }
        return null;
    }

    private static string? FormatMembership(Membership membership)
    {
        if (membership.ContactGroupMembership != null) { return $"group: {membership.ContactGroupMembership.ContactGroupResourceName ?? membership.ContactGroupMembership.ContactGroupId}"; }
        if (membership.DomainMembership != null) { return $"domain (in viewer domain: {membership.DomainMembership.InViewerDomain})"; }
        return null;
    }

    private static string? FormatSource(Source source)
    {
        DateTimeOffset? updated = source.UpdateTimeDateTimeOffset;
        string when = updated.HasValue ? updated.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "n/a";
        return $"{source.Type ?? "unknown"} (updated {when})";
    }

    private static string? WithType(string? value, string? type)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        if (string.IsNullOrWhiteSpace(type)) { return value; }
        return $"{value} ({type})";
    }

    private static DateTimeOffset? GetModifiedTime(Person person)
    {
        // Use the most recent update time across all of the contact's sources.
        IList<Source>? sources = person.Metadata?.Sources;
        if (sources == null) { return null; }

        DateTimeOffset? latest = null;
        foreach (Source source in sources)
        {
            DateTimeOffset? updated = source.UpdateTimeDateTimeOffset;
            if (updated.HasValue && (latest == null || updated.Value > latest.Value)) { latest = updated.Value; }
        }

        return latest;
    }

    private static void PrintValues(string label, IEnumerable<string?>? values)
    {
        if (values == null) { return; }
        List<string> list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        if (list.Count == 0) { return; }
        if (list.Count == 1)
        {
            Console.WriteLine($"    {label}: {list[0]}");
        }
        else
        {
            Console.WriteLine($"    {label}:");
            foreach (string value in list) { Console.WriteLine($"      - {value}"); }
        }
    }

    private static void PrintField(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return; }
        Console.WriteLine($"    {label}: {value}");
    }
}
