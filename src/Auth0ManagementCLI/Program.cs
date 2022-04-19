using System.Text.Json;
using Auth0.Core.Exceptions;
using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Models.Actions;
using Auth0.ManagementApi.Paging;
using Cocona;
using Mapster;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RestSharp;
using static Crayon.Output;

public static class Program
{
    public static async Task Main(params string[] args)
    {
        var config = GetConfig();
        var domain = GetAuth0ConfigValue(config, "domain");
        var auth0BaseUrl = new UriBuilder("https", domain).Uri.ToString();
        var clientId = GetAuth0ConfigValue(config, "clientId");
        var clientSecret = GetAuth0ConfigValue(config, "clientSecret");


        var accessToken = await GetAccessToken(auth0BaseUrl, clientId, clientSecret);
        var managementClient = new ManagementApiClient(accessToken, domain);
        var app = CoconaApp.Create();
        AddOrgCommands(app, managementClient);
        AddUserCommands(app, managementClient);
        AddClientCommands(app, managementClient);
        AddExportCommands(app, managementClient);
        AddRoleCommands(app, managementClient);
        AddApiCommands(app, managementClient);
        AddRuleCommmands(app, managementClient);
        AddActionCommands(app, managementClient);

        await app.RunAsync();
    }

    private static void AddActionCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-actions", async ([Argument] bool? deployed, [Argument] int? page) =>
        {
            page ??= 0;
            var actions = await managementClient.Actions.GetAllAsync(new GetActionsRequest() { Deployed = deployed }, new PaginationInfo(page.Value));
            PrintResponse(actions);
        });

        app.AddCommand("list-action-triggers", async () =>
        {
            var actions = await managementClient.Actions.GetAllTriggersAsync();
            PrintResponse(actions);
        });

        app.AddCommand("list-action-trigger-bindings", async ([Argument] string triggerId, [Argument] int? page) =>
        {
            try
            {
                page ??= 0;
                var triggerBindings = await managementClient.Actions.GetAllTriggerBindingsAsync(triggerId, new PaginationInfo(page.Value));
                PrintResponse(triggerBindings);
            }
            catch (ErrorApiException e) when (e.ApiError.ErrorCode == "invalid_uri")
            {
                Console.WriteLine(Red("Invalid triggerId"));
            }
        });
    }

    private static void AddRuleCommmands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-rules", async ([Argument]bool? enabledOnly, [Argument] int? page) =>
        {
            page = page ?? 0;
            var rules = await managementClient.Rules.GetAllAsync(new GetRulesRequest{Enabled = enabledOnly}, new PaginationInfo(page.Value));
            PrintResponse(rules);
        });
    }

    private static void AddApiCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-apis", async ([Argument] int? page) =>
        {
            page = page ?? 0;
            var apis = await managementClient.ResourceServers.GetAllAsync(new PaginationInfo(page.Value));
            PrintResponse(apis);
        });
    }

    private static async Task AddRoleCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("list-roles", async ([Argument] int? page) =>
        {
            page ??= 0;
            var roles = await managementClient.Roles.GetAllAsync(new GetRolesRequest(), new PaginationInfo(page.Value));
            PrintResponse(roles);
        });

        app.AddCommand("list-permissions", async ([Argument] string role, [Argument] int? page) =>
        {
            page ??= 0;
            var permissions = await managementClient.Roles.GetPermissionsAsync(role, new PaginationInfo(page.Value));
            PrintResponse(permissions);
        });

    }

    private static void AddExportCommands(CoconaApp app, ManagementApiClient managementSourceClient)
    {
        app.AddCommand("export-tenant", async ([Argument("target-domain", Description = "The domain of the tenant you are exporting to")] string targetDomain,
            [Argument("target-client-id", Description = "The client Id of the client to call the management api on the target tenant")] string targetManagementApiClientId,
            [Argument("target-client-secret", Description = "The client secret of the client to call the management api on the target tenant")] string targetManagementApiClientSecret) =>
        {
            var baseUrl = new UriBuilder("https", targetDomain).Uri.ToString();
            var token = await GetAccessToken(baseUrl, targetManagementApiClientId, targetManagementApiClientSecret);
            var managementTargetClient = new ManagementApiClient(token, targetDomain);


            // auth0 doesn't have a native way to have M2M clients per org, so meta_data is the suggested way to handle this
            // here we assume that org_id is the key used for this metadata (change to whatever you use if this doesn't match your usecase)
            // we will map the source org id with target org id for when we map clients
            var orgIdMapping = new Dictionary<string, string>();

            var orgCount = await ExportOrganizations(managementSourceClient, managementTargetClient, orgIdMapping);
            var clientCount = await ExportClients(managementSourceClient, managementTargetClient, orgIdMapping);
            var apiCount = await ExportAPIs(managementSourceClient, managementTargetClient);
            var roleCount = await ExportRoles(managementSourceClient, managementTargetClient);
            var ruleCount = await ExportRules(managementSourceClient, managementTargetClient);
            var actionCount = await ExportActions(managementSourceClient, managementTargetClient);
            var flowCount = await ExportFlows(managementSourceClient, managementTargetClient);

             Console.WriteLine($"{orgCount} orgs exported");
             Console.WriteLine($"{clientCount} clients exported");
             Console.WriteLine($"{apiCount} apis exported");
             Console.WriteLine($"{roleCount} roles exported");
             Console.WriteLine($"{ruleCount} rules exported");
             Console.WriteLine($"{actionCount} actions exported");
             Console.WriteLine($"{flowCount} flow-action-trigger-bindings exported");

        });
    }

    private static async Task <int>ExportFlows(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient)
    {
        var page = 0;
        var count = 0;

        var triggers = await managementTargetClient.Actions.GetAllTriggersAsync();

        foreach (var trigger in triggers.Where(t=>t.Status == "CURRENT"))
        {
            int bindingPage = 0;
            while (true)
            {
                var bindings = await managementSourceClient.Actions.GetAllTriggerBindingsAsync(trigger.Id, new PaginationInfo(bindingPage));

               
                await managementTargetClient.Actions.UpdateTriggerBindingsAsync(trigger.Id, new UpdateTriggerBindingsRequest
                {
                    Bindings = bindings.Select(b => new UpdateTriggerBindingEntry
                    {
                        DisplayName = b.DisplayName,
                        Ref = new UpdateTriggerBindingEntry.BindingRef
                        {
                            Value = b.Action.Name,
                            Type = "action_name"
                        }
                    }).ToList()
                });

                count++;
                bindingPage++;
                if (bindings.Count < 100)
                    break;
                await Task.Delay(2000);
            }

        }

        return count;
    }

    private static async Task<int> ExportActions(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient)
    {
        var page = 0;
        var count = 0;
        while (true)
        {
            var actions = await managementSourceClient.Actions.GetAllAsync(new GetActionsRequest(), new PaginationInfo(page, 100, true));
            foreach (var action in actions)
            {
                var createActionRequest = action.Adapt<CreateActionRequest>();
                var newAction = await managementTargetClient.Actions.CreateAsync(createActionRequest);
                await managementTargetClient.Actions.DeployAsync(newAction.Id);
                count++;
            }
            if (actions.Count < 100)
                break;
            page++;
        }
        return count;
    }

    private static async Task<int> ExportRules(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient)
    {
        var page = 0;
        var count = 0;
        while (true)
        {
            var rules = await managementSourceClient.Rules.GetAllAsync(new GetRulesRequest(), new PaginationInfo(page, 100, true));
            foreach (var rule in rules)
            {
                var createRuleRequest = rule.Adapt<RuleCreateRequest>();
                await managementTargetClient.Rules.CreateAsync(createRuleRequest);
                count++;
            }
            if (rules.Paging.Length < rules.Paging.Limit)
                break;
            page++;
        }
        return count;
    }

    private static async Task<int> ExportRoles(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient)
    {
        var page = 0;
        var count = 0;
        while (true)
        {
            var roles = await managementSourceClient.Roles.GetAllAsync(new GetRolesRequest(), new PaginationInfo(page, 100, true));
            foreach (var role in roles)
            {
                var createRoleRequest = role.Adapt<RoleCreateRequest>();
                var newRole = await managementTargetClient.Roles.CreateAsync(createRoleRequest);

                var permissionPage = 0;
                while (true)
                {
                    var permissions = await managementSourceClient.Roles.GetPermissionsAsync(role.Id, new PaginationInfo(permissionPage, 100, true));

                    var permissionIdentities = permissions.Select(p => new PermissionIdentity { Identifier = p.Identifier, Name = p.Name }).ToList();
                    await managementTargetClient.Roles.AssignPermissionsAsync(newRole.Id, new AssignPermissionsRequest { Permissions = permissionIdentities });
                    permissionPage++;
                    if (permissions.Paging.Length < permissions.Paging.Limit)
                        break;
                }
                count++;
            }
            if (roles.Paging.Length < roles.Paging.Limit)
                break;
            page++;
        }
        return count;
    }

    private static async Task<int> ExportAPIs(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient)
    {
        var page = 0;
        var count = 0;
        while (true)
        {
            var apis = await managementSourceClient.ResourceServers.GetAllAsync( new PaginationInfo(page, 100, true));
            foreach (var api in apis)
            {
               
                if (api.Name != "Auth0 Management API")
                {
                    var createApiRequest = api.Adapt<ResourceServerCreateRequest>();
                    createApiRequest.SigningSecret = null;
                    createApiRequest.SigningAlgorithm = null;
                    createApiRequest.Id = null;

                    await managementTargetClient.ResourceServers.CreateAsync(createApiRequest);
                    count++;
                }
            }
            if (apis.Paging.Length < apis.Paging.Limit)
                break;
            page++;
        }
        return count;
    }

    private static async Task<int> ExportClients(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient, Dictionary<string, string> orgIdMapping)
    {
        var page = 0;
        var count = 0;
        while (true)
        {
            var clients = await managementSourceClient.Clients.GetAllAsync(new GetClientsRequest(), new PaginationInfo(page, 100, true));
            foreach (var client in clients)
            {
                var createClientRequest = client.Adapt<ClientCreateRequest>();
                if (client.ClientMetaData?.org_id != null)
                {
                    createClientRequest.ClientMetaData.org_id = orgIdMapping[createClientRequest.ClientMetaData.org_id.ToString()];
                }
                createClientRequest.ClientSecret = null;
                await managementTargetClient.Clients.CreateAsync(createClientRequest);
                count++;
            }
            if (clients.Paging.Length < clients.Paging.Limit)
                break;
            page++;
        }
        return count;
    }

    private static async Task<int> ExportOrganizations(ManagementApiClient managementSourceClient, ManagementApiClient managementTargetClient, Dictionary<string, string> orgIdMapping)
    {
        var page = 0;
        var count = 0;
        while (true)
        {
            var organizations = await managementSourceClient.Organizations.GetAllAsync(new PaginationInfo(page, 100, true));
            foreach (var organization in organizations)
            {
                var createOrgRequest = organization.Adapt<OrganizationCreateRequest>();
                var response = await managementTargetClient.Organizations.CreateAsync(createOrgRequest);
                orgIdMapping[organization.Id] = response.Id;
                count++;
            }
            if (organizations.Paging.Length < organizations.Paging.Limit)
                break;
            page++;
        }
        return count;
    }

    private static void AddClientCommands(CoconaApp app, ManagementApiClient managementClient)
    {
        app.AddCommand("get-token", async () =>
        {
            var config = GetConfig();
            var domain = GetAuth0ConfigValue(config, "domain");
            var auth0BaseUrl = new UriBuilder("https", domain).Uri.ToString();
            var clientId = GetAuth0ConfigValue(config, "clientId");
            var clientSecret = GetAuth0ConfigValue(config, "clientSecret");
            var accessToken = await GetAccessToken(auth0BaseUrl, clientId, clientSecret);
            PrintResponse(accessToken);
        });

        app.AddCommand("list-clients", async ([Argument]int? page) =>
        {
            page ??= 0;
            var clients = await managementClient.Clients.GetAllAsync(new GetClientsRequest(), new PaginationInfo(page.Value, 100, true));
            PrintResponse(clients);
        });


        app.AddCommand("list-client-application-types", () =>
        {
            PrintResponse(Enum.GetNames<ClientApplicationType>());
        });

        app.AddCommand("list-client-grants", async ([Argument] int? page) =>
        {
            page ??= 0;
            var clientGrants = await managementClient.ClientGrants.GetAllAsync(new GetClientGrantsRequest(), new PaginationInfo(page.Value, 100, true));
            PrintResponse(clientGrants);
        });

        app.AddCommand("create-client", async ([Argument(Description = "use 'list-client-application-types' for the list of valid values")] string applicationType,
            [Argument] string? description,
            [Argument(Description = "a comma delimited list of grant types")] string? allowedGrantTypesCommaDelimited,
            [Argument(Description = "a comma delimited list of callbacks")] string? allowedCallbacksCommaDelimited,
            [Argument(Description = "a comma delimited list of allowed origins")] string? allowedOriginsCommaDelimited,
            [Argument(Description = "a comma delimited list of allowed origins")] string? allowedLogoutUrlsCommaDelimited) =>
        {
            var applicationTypeEnum = Enum.Parse<ClientApplicationType>(applicationType);
            string[] allowedGrantTypes = null;
            if (!String.IsNullOrEmpty(allowedGrantTypesCommaDelimited))
            {
                allowedGrantTypes = allowedGrantTypesCommaDelimited.Split(',');
            }
            string[] allowedCallbacks = null;
            if (!String.IsNullOrEmpty(allowedCallbacksCommaDelimited))
            {
                allowedCallbacks = allowedCallbacksCommaDelimited.Split(',');
            }
            string[] allowedOrigins = null;
            if (!String.IsNullOrEmpty(allowedOriginsCommaDelimited))
            {
                allowedOrigins = allowedOriginsCommaDelimited.Split(',');
            }
            string[] allowedLogoutUrls = null;
            if (!String.IsNullOrEmpty(allowedLogoutUrlsCommaDelimited))
            {
                allowedLogoutUrls = allowedLogoutUrlsCommaDelimited.Split(',');
            }

            var request = new ClientCreateRequest
            {
                ApplicationType = applicationTypeEnum,
                AllowedOrigins = allowedOrigins,
                AllowedLogoutUrls = allowedLogoutUrls,
                Description = description,
                Callbacks = allowedCallbacks,
                GrantTypes = allowedGrantTypes,
                //todo
            };

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
        Console.WriteLine(Cyan(JsonConvert.SerializeObject(response, Formatting.Indented)));
    }
}