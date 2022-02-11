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
        var auth0BaseUrl = config.GetSection("auth0").GetValue<string>("baseUrl");
        var domain = config.GetSection("auth0").GetValue<string>("domain");
        var clientId = config.GetSection("auth0").GetValue<string>("clientId");
        var clientSecret = config.GetSection("auth0").GetValue<string>("clientSecret");


        var accessToken = await GetAccessToken(auth0BaseUrl, clientId, clientSecret);
        var managementClient = new ManagementApiClient(accessToken, domain);
        var app = CoconaApp.Create();
        AddOrgCommands(app, managementClient);
        AddUserCommands(app, managementClient);
        await app.RunAsync();
    }

    private static void AddUserCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-users", async () =>
        {
            var users = await managementClient.Users.GetAllAsync(new GetUsersRequest());
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

        app.AddCommand("list-orgs", async () =>
        {
            var organizations = await managementClient.Organizations.GetAllAsync(new PaginationInfo(0, 100, true));
            PrintResponse(organizations);
        });

        app.AddCommand("invite-org-member",
            async
                ([Argument] string orgId, [Argument] string clientId, [Argument] string inviteeEmail, [Option("inviter")] string? inviter,
                    [Option("send-email")] bool sendEmail) =>
                {
                    inviter ??= "Spresso";

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
                ([Argument] string orgId) =>
                {
                    var members = await managementClient.Organizations.GetAllMembersAsync(orgId, new PaginationInfo(perPage: 100, includeTotals: true));

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

    private static void PrintResponse(object response)
    {
        Console.WriteLine("Response\n=====================\n");
        Console.WriteLine(JsonConvert.SerializeObject(response));
    }
}