using System.Net;
using System.Net.Http;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.TestHelpers;
using Polly;

namespace NzbDrone.Core.Test.ArrIntegration.Webhook;

[TestFixture]
public class ArrWebhookRegistrationTest
{
    private IConfigFileProvider _configFileProvider;
    private IConfigService _configService;
    private ArrWebhookRegistration _registration;

    [SetUp]
    public void Setup()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configService = Substitute.For<IConfigService>();
        _configFileProvider.BindAddress.Returns("localhost");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");
        _registration = new ArrWebhookRegistration(_configFileProvider, _configService);
    }

    [Test]
    public void RegisterWebhook_should_return_true_when_webhook_disabled()
    {
        var connection = new ArrConnectionDefinition { WebhookEnabled = false };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void GetSeedarrBaseUrl_should_use_bind_address_and_port()
    {
        _configFileProvider.BindAddress.Returns("myhost");
        _configFileProvider.Port.Returns(8080);
        _configFileProvider.UrlBase.Returns("");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://myhost:8080"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_replace_wildcard_with_hostname()
    {
        _configFileProvider.BindAddress.Returns("*");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        var expectedHost = Dns.GetHostName();
        Assert.That(result, Is.EqualTo($"http://{expectedHost}:9898"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_replace_0000_with_hostname()
    {
        _configFileProvider.BindAddress.Returns("0.0.0.0");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        var expectedHost = Dns.GetHostName();
        Assert.That(result, Is.EqualTo($"http://{expectedHost}:9898"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_use_env_host_when_available()
    {
        _configFileProvider.BindAddress.Returns("*");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");

        System.Environment.SetEnvironmentVariable("SEEDARR_HOST", "my-custom-host");
        try
        {
            var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var connection = new ArrConnectionDefinition();
            var result = (string)method.Invoke(_registration, new object[] { connection });

            Assert.That(result, Is.EqualTo("http://my-custom-host:9898"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("SEEDARR_HOST", null);
        }
    }

    [Test]
    public void GetSeedarrBaseUrl_should_use_webhook_host_from_connection_when_available()
    {
        _configFileProvider.BindAddress.Returns("*");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");
        var connection = new ArrConnectionDefinition { WebhookHost = "config-external-host" };

        // The external host should override the env variable
        System.Environment.SetEnvironmentVariable("SEEDARR_HOST", "my-env-host");
        try
        {
            var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (string)method.Invoke(_registration, new object[] { connection });

            Assert.That(result, Is.EqualTo("http://config-external-host:9898"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("SEEDARR_HOST", null);
        }
    }

    [Test]
    public void GetSeedarrBaseUrl_should_include_url_base()
    {
        _configFileProvider.BindAddress.Returns("localhost");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("/seedarr");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://localhost:9898/seedarr"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_handle_null_url_base()
    {
        _configFileProvider.BindAddress.Returns("localhost");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns((string)null);

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://localhost:9898"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_preserve_specific_bind_address()
    {
        _configFileProvider.BindAddress.Returns("192.168.1.100");
        _configFileProvider.Port.Returns(9090);
        _configFileProvider.UrlBase.Returns("");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://192.168.1.100:9090"));
    }

    [Test]
    public void RegisterWebhook_should_use_v1_api_for_lidarr()
    {
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Lidarr",
            Url = "http://nonexistent.invalid:8686",
            ApiKey = "test-key"
        };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void RegisterWebhook_should_use_v3_api_for_sonarr()
    {
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = "http://nonexistent.invalid:8989",
            ApiKey = "test-key"
        };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnregisterWebhook_should_return_true_when_no_existing_webhook_found()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://nonexistent.invalid:8989",
            ApiKey = "test-key"
        };

        var result = _registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_should_use_v3_api_for_radarr()
    {
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Radarr",
            Url = "http://nonexistent.invalid:7878",
            ApiKey = "test-key"
        };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnregisterWebhook_should_return_true_for_lidarr_with_no_webhook()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Lidarr",
            Url = "http://nonexistent.invalid:8686",
            ApiKey = "test-key"
        };

        var result = _registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void UnregisterWebhook_should_return_true_for_radarr_with_no_webhook()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Radarr",
            Url = "http://nonexistent.invalid:7878",
            ApiKey = "test-key"
        };

        var result = _registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_should_return_false_when_url_is_unreachable()
    {
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = "http://nonexistent.invalid:8989",
            ApiKey = "api-key-123"
        };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetSeedarrBaseUrl_should_construct_correct_url_with_all_parts()
    {
        _configFileProvider.BindAddress.Returns("myserver");
        _configFileProvider.Port.Returns(1234);
        _configFileProvider.UrlBase.Returns("/base");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://myserver:1234/base"));
    }

    [Test]
    public void FindExistingWebhook_should_return_null_when_url_unreachable()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://nonexistent.invalid:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(_registration, new object[] { connection, "v3", "http://localhost:9898/api/v1/webhook/arr" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindExistingWebhook_should_return_null_for_lidarr_with_unreachable_url()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Lidarr",
            Url = "http://nonexistent.invalid:8686",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(_registration, new object[] { connection, "v1", "http://localhost:9898/api/v1/webhook/arr" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RegisterWebhook_should_use_correct_webhook_url()
    {
        _configFileProvider.BindAddress.Returns("seedarr-host");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("/seedarr");

        var registration = new ArrWebhookRegistration(_configFileProvider, _configService);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = "http://nonexistent.invalid:8989",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnregisterWebhook_should_handle_exception_gracefully()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = null,
            Url = null,
            ApiKey = null
        };

        var result = _registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True.Or.False);
    }

    [Test]
    public void RegisterWebhook_should_return_false_when_url_is_null_and_webhook_enabled()
    {
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = null,
            ApiKey = "test-key"
        };

        // FindExistingWebhook catches the internal exception and returns null.
        // Policy.Execute then tries to POST to a relative URI without a base address,
        // throwing InvalidOperationException which the outer catch returns false for.
        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnregisterWebhook_should_return_true_when_no_webhook_exists_for_null_url()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = null,
            ApiKey = "test-key"
        };

        // FindExistingWebhook catches the HTTP exception and returns null.
        // Since no existing ID is found (!existingId.HasValue), returns true immediately.
        var result = _registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_should_return_true_immediately_when_webhook_disabled_regardless_of_config()
    {
        // When WebhookEnabled = false, RegisterWebhook returns immediately without
        // consulting config or making any network call.
        _configFileProvider.BindAddress.Returns("unreachable-host");
        _configFileProvider.Port.Returns(1);
        _configFileProvider.UrlBase.Returns("");

        var connection = new ArrConnectionDefinition { WebhookEnabled = false, ArrType = "Sonarr" };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void GetSeedarrBaseUrl_should_work_with_port_80()
    {
        _configFileProvider.BindAddress.Returns("localhost");
        _configFileProvider.Port.Returns(80);
        _configFileProvider.UrlBase.Returns("");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://localhost:80"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_work_with_port_443()
    {
        _configFileProvider.BindAddress.Returns("localhost");
        _configFileProvider.Port.Returns(443);
        _configFileProvider.UrlBase.Returns("");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://localhost:443"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_include_url_base_with_leading_slash()
    {
        _configFileProvider.BindAddress.Returns("localhost");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("/app/seedarr");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://localhost:9898/app/seedarr"));
    }

    [Test]
    public void RegisterWebhook_should_return_true_immediately_when_webhook_disabled_with_null_url()
    {
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = false,
            ArrType = "Radarr",
            Url = null,
            ApiKey = null
        };

        var result = _registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_should_use_v1_for_lidarr_and_v3_for_radarr()
    {
        var lidarrConnection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Lidarr",
            Url = "http://nonexistent.invalid:8686",
            ApiKey = "key"
        };
        var radarrConnection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Radarr",
            Url = "http://nonexistent.invalid:7878",
            ApiKey = "key"
        };

        var lidarrResult = _registration.RegisterWebhook(lidarrConnection);
        var radarrResult = _registration.RegisterWebhook(radarrConnection);

        Assert.That(lidarrResult, Is.False);
        Assert.That(radarrResult, Is.False);
    }

    [Test]
    public void UnregisterWebhook_should_use_v1_for_lidarr_and_return_true_when_no_webhook()
    {
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Lidarr",
            Url = "http://nonexistent.invalid:8686",
            ApiKey = "test-key"
        };

        var result = _registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_should_construct_correct_seedarr_webhook_url_in_notification_body()
    {
        _configFileProvider.BindAddress.Returns("myserver");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");

        var registration = new ArrWebhookRegistration(_configFileProvider, _configService);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Radarr",
            Url = "http://nonexistent.invalid:7878",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetSeedarrBaseUrl_wildcard_is_replaced_before_url_is_used_in_webhook()
    {
        _configFileProvider.BindAddress.Returns("*");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("/seedarr");

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var connection = new ArrConnectionDefinition();
        var result = (string)method.Invoke(_registration, new object[] { connection });

        var expectedHost = Dns.GetHostName();
        Assert.That(result, Is.EqualTo($"http://{expectedHost}:9898/seedarr"));
    }

    // --- Constructor-injection tests (inject mock HttpClient + fresh policy) ---

    private ArrWebhookRegistration CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new ArrWebhookRegistration(_configFileProvider, _configService, httpClient, policy);
    }

    [Test]
    public void RegisterWebhook_with_injected_client_should_return_true_when_webhook_already_registered()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":42,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://localhost:9898/api/v1/webhook/arr""},{""name"":""method"",""value"":1}]}]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_with_injected_client_should_return_true_when_post_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"[]");
        handler.Enqueue(HttpStatusCode.Created, @"{""id"":99,""name"":""Seedarr""}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegisterWebhook_with_injected_client_should_return_false_when_post_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"[]");
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void RegisterWebhook_with_injected_client_uses_v1_api_for_lidarr()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"[]");
        handler.Enqueue(HttpStatusCode.OK, @"{""id"":7}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Lidarr",
            Url = "http://lidarr:8686",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void FindExistingWebhook_with_injected_client_should_return_id_when_found()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":55,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://localhost:9898/api/v1/webhook/arr""}]}]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(registration, new object[]
        {
            connection, "v3", "http://localhost:9898/api/v1/webhook/arr"
        });

        Assert.That(result, Is.EqualTo(55));
    }

    [Test]
    public void FindExistingWebhook_with_injected_client_should_return_null_when_name_mismatch()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":10,""name"":""OtherPlugin"",""fields"":[{""name"":""url"",""value"":""http://localhost:9898/api/v1/webhook/arr""}]}]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(registration, new object[]
        {
            connection, "v3", "http://localhost:9898/api/v1/webhook/arr"
        });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindExistingWebhook_with_injected_client_should_return_null_when_url_field_mismatch()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":20,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://other-host:9999/api/v1/webhook/arr""}]}]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(registration, new object[]
        {
            connection, "v3", "http://localhost:9898/api/v1/webhook/arr"
        });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindExistingWebhook_with_injected_client_should_return_null_when_get_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "bad-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(registration, new object[]
        {
            connection, "v3", "http://localhost:9898/api/v1/webhook/arr"
        });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindExistingWebhook_with_injected_client_should_return_null_when_empty_list()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"[]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(registration, new object[]
        {
            connection, "v3", "http://localhost:9898/api/v1/webhook/arr"
        });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void UnregisterWebhook_with_injected_client_should_return_true_when_delete_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":77,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://localhost:9898/api/v1/webhook/arr""}]}]");
        handler.Enqueue(HttpStatusCode.OK, @"{}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var result = registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void UnregisterWebhook_with_injected_client_should_return_false_when_delete_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":88,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://localhost:9898/api/v1/webhook/arr""}]}]");
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var result = registration.UnregisterWebhook(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnregisterWebhook_with_injected_client_should_return_true_when_no_webhook_registered()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"[]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "test-key"
        };

        var result = registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void UnregisterWebhook_with_injected_client_uses_v1_for_lidarr()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":11,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://localhost:9898/api/v1/webhook/arr""}]}]");
        handler.Enqueue(HttpStatusCode.OK, @"{}");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Lidarr",
            Url = "http://lidarr:8686",
            ApiKey = "test-key"
        };

        var result = registration.UnregisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void FindExistingWebhook_with_injected_client_should_handle_notification_with_no_fields_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"[{""id"":30,""name"":""Seedarr""}]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookRegistration).GetMethod("FindExistingWebhook",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (int?)method.Invoke(registration, new object[]
        {
            connection, "v3", "http://localhost:9898/api/v1/webhook/arr"
        });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RegisterWebhook_with_injected_client_uses_url_base_in_webhook_url()
    {
        _configFileProvider.BindAddress.Returns("seedarr-host");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("/app");

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""id"":5,""name"":""Seedarr"",""fields"":[{""name"":""url"",""value"":""http://seedarr-host:9898/app/api/v1/webhook/arr""}]}]");

        var registration = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            WebhookEnabled = true,

            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var result = registration.RegisterWebhook(connection);

        Assert.That(result, Is.True);
    }

    [Test]
    public void GetSeedarrBaseUrl_should_use_full_url_when_webhook_host_contains_protocol()
    {
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");
        var connection = new ArrConnectionDefinition { WebhookHost = "http://seedarr.tanoki.online" };

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(_registration, new object[] { connection });

        Assert.That(result, Is.EqualTo("http://seedarr.tanoki.online"));
    }

    [Test]
    public void GetSeedarrBaseUrl_should_ignore_hex_container_id_in_bind_address()
    {
        _configFileProvider.BindAddress.Returns("43aed7b7e138");
        _configFileProvider.Port.Returns(9898);
        _configFileProvider.UrlBase.Returns("");
        var connection = new ArrConnectionDefinition();

        var method = typeof(ArrWebhookRegistration).GetMethod("GetSeedarrBaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(_registration, new object[] { connection });

        var expectedHost = Dns.GetHostName();
        Assert.That(result, Is.EqualTo($"http://{expectedHost}:9898"));
    }
}
