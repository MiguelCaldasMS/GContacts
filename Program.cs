// GContacts - lists Google Contacts (name, email, added date, modified date) via the Google People API.
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
    // Full read/write access so the app can also create, update, and delete contacts (write operations not yet implemented).
    private static readonly string[] Scopes = { PeopleServiceService.Scope.Contacts };
    private const string ApplicationName = "GContacts";
    private const string CredentialsFile = "client_secret.json";
    private const int DisplayLimit = 100;

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

            DisplayContacts(contacts);
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
            request.PersonFields = "names,emailAddresses,metadata";
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

    private static void DisplayContacts(IReadOnlyList<Person> contacts)
    {
        int count = Math.Min(contacts.Count, DisplayLimit);
        PrintRow("#", "Name", "Email", "Added", "Modified");
        Console.WriteLine(new string('-', 92));

        for (int i = 0; i < count; i++)
        {
            Person person = contacts[i];
            string name = person.Names?.FirstOrDefault()?.DisplayName ?? "(no name)";
            string email = person.EmailAddresses?.FirstOrDefault()?.Value ?? "(no email)";
            // The People API does not expose a contact creation timestamp, so "Added" is unavailable.
            string added = "N/A";
            DateTimeOffset? modified = GetModifiedTime(person);
            string modifiedText = modified.HasValue ? modified.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "N/A";
            PrintRow((i + 1).ToString(), Truncate(name, 26), Truncate(email, 32), added, modifiedText);
        }

        if (contacts.Count > DisplayLimit) { Console.WriteLine($"\n(Showing the first {DisplayLimit} of {contacts.Count} contacts.)"); }
        Console.WriteLine("\nNote: 'Added' date is not provided by the Google People API; only the last-modified time is available.");
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

    private static void PrintRow(string index, string name, string email, string added, string modified)
    {
        Console.WriteLine($"{index,-4} {name,-26} {email,-32} {added,-10} {modified,-16}");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) { return value; }
        return value.Substring(0, maxLength - 1) + "\u2026";
    }
}
