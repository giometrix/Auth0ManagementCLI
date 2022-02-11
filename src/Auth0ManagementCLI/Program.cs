using System.Text.Json;
using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Paging;
using Cocona;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RestSharp;

public static class Program
{
    public static async Task Main(params string[] args)
    {
        var config = GetConfig();
        var auth0BaseUrl = GetAuth0ConfigValue(config, "baseUrl");
        var domain = GetAuth0ConfigValue(config, "domain");
        var clientId = GetAuth0ConfigValue(config, "clientId");
        var clientSecret = GetAuth0ConfigValue(config, "clientSecret");


        var accessToken = await GetAccessToken(auth0BaseUrl, clientId, clientSecret);
        var managementClient = new ManagementApiClient(accessToken, domain);
        var app = CoconaApp.Create();
        AddOrgCommands(app, managementClient);
        AddUserCommands(app, managementClient);
        AddClientCommands(app, managementClient);
        await app.RunAsync();
    }

    private static void AddClientCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-clients", async ([Argument]int? page) =>
        {
            page ??= 0;
            var clients = await managementClient.Clients.GetAllAsync(new GetClientsRequest(), new PaginationInfo(page.Value, 100, true));
            PrintResponse(clients);
        });
    }

    private static void AddUserCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-users", async ([Argument] int? page) =>
        {
            page ??= 0;
            var users = await managementClient.Users.GetAllAsync(new GetUsersRequest(), new PaginationInfo(page.Value, 100, true));
            PrintResponse(users);
        });
    }

    private static void AddOrgCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("create-org", async ([Argument] string name, [Argument] string displayName) =>
        {
            var request = new OrganizationCreateRequest
            {
                Name = name,
                DisplayName = displayName
            };
            var organization = await managementClient.Organizations.CreateAsync(request);
            PrintResponse(organization);
        });

        app.AddCommand("list-orgs", async ([Argument] int? page) =>
        {
            page ??= 0;
            var organizations = await managementClient.Organizations.GetAllAsync(new PaginationInfo(page.Value, 100, true));
            PrintResponse(organizations);
        });

        app.AddCommand("invite-org-member",
            async
                ([Argument] string orgId, [Argument] string clientId, [Argument] string inviteeEmail, [Option("inviter")] string? inviter,
                    [Option("send-email")] bool sendEmail) =>
                {
                    inviter ??= "Welcome";

                    var request = new OrganizationCreateInvitationRequest
                    {
                        Invitee = new OrganizationInvitationInvitee { Email = inviteeEmail },
                        Inviter = new OrganizationInvitationInviter { Name = inviter },
                        SendInvitationEmail = sendEmail,
                        ClientId = clientId
                    };
                    var invite = await managementClient.Organizations.CreateInvitationAsync(orgId, request);

                    PrintResponse(invite);
                });

        app.AddCommand("list-org-members",
            async
                ([Argument] string orgId, [Argument] int? page) =>
                {
                    page ??= 0;

                    var members = await managementClient.Organizations.GetAllMembersAsync(orgId, new PaginationInfo(pageNo: page.Value, perPage: 100, includeTotals: true));

                    PrintResponse(members);
                });
    }

    private static async Task<string> GetAccessToken(string auth0BaseUrl, string clientId, string clientSecret)
    {
        var client = new RestClient(auth0BaseUrl);
        var request = new RestRequest("oauth/token", Method.Post);

        var apiPath = "/api/v2/";
        if (auth0BaseUrl.EndsWith('/'))
        {
            apiPath = apiPath.Substring(1);
        }


        request.AddJsonBody(new
        {
            client_id = clientId,
            client_secret = clientSecret,
            audience = auth0BaseUrl + apiPath,
            grant_type = "client_credentials"
        });

        var response = await client.ExecuteAsync(request);

        if (!response.IsSuccessful)
        {
            throw new Exception($"Getting token failed:  {response.Content}");
        }

        var json = JsonDocument.Parse(response.Content!);

        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private static IConfiguration GetConfig()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appSettings.json")
            .AddJsonFile("appSettings.local.json", true)
            .AddEnvironmentVariables()
            .Build();
        return config;
    }

    private static string GetAuth0ConfigValue(IConfiguration config, string key) => config.GetSection("auth0").GetValue<string>(key);

    private static void PrintResponse(object response)
    {
        Console.WriteLine("Response\n=====================\n");
        Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));
    }
}