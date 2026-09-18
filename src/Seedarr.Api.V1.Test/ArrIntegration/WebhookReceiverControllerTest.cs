using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.ArrIntegration;

namespace Seedarr.Api.V1.Test.ArrIntegration;

[TestFixture]
public class WebhookReceiverControllerTest
{
    private const string ExpectedApiKey = "test-secret-key-12345";

    private IArrWebhookService _webhookService;
    private IConfigFileProvider _configFileProvider;
    private WebhookReceiverController _controller;

    [SetUp]
    public void SetUp()
    {
        _webhookService = Substitute.For<IArrWebhookService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.ApiKey.Returns(ExpectedApiKey);

        _controller = new WebhookReceiverController(_webhookService, _configFileProvider);
    }

    private void SetupRequest(
        string xApiKeyHeader = null,
        string apiKeyHeader = null,
        string authorizationHeader = null,
        string apikeyQuery = null,
        string apiKeyQuery = null,
        string accessTokenQuery = null)
    {
        var httpContext = new DefaultHttpContext();

        if (xApiKeyHeader != null)
        {
            httpContext.Request.Headers["X-Api-Key"] = xApiKeyHeader;
        }

        if (apiKeyHeader != null)
        {
            httpContext.Request.Headers["ApiKey"] = apiKeyHeader;
        }

        if (authorizationHeader != null)
        {
            httpContext.Request.Headers["Authorization"] = authorizationHeader;
        }

        var queryDict = new Dictionary<string, StringValues>();
        if (apikeyQuery != null)
        {
            queryDict["apikey"] = apikeyQuery;
        }

        if (apiKeyQuery != null)
        {
            queryDict["api_key"] = apiKeyQuery;
        }

        if (accessTokenQuery != null)
        {
            queryDict["access_token"] = accessTokenQuery;
        }

        if (queryDict.Count > 0)
        {
            httpContext.Request.Query = new QueryCollection(queryDict);
        }

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Test]
    public void ReceiveArrWebhook_with_valid_X_Api_Key_header_should_return_ok()
    {
        SetupRequest(xApiKeyHeader: ExpectedApiKey);
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_with_valid_ApiKey_header_should_return_ok()
    {
        SetupRequest(apiKeyHeader: ExpectedApiKey);
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_with_valid_Bearer_auth_should_return_ok()
    {
        SetupRequest(authorizationHeader: $"Bearer {ExpectedApiKey}");
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_with_valid_apikey_query_parameter_should_return_ok()
    {
        SetupRequest(apikeyQuery: ExpectedApiKey);
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_with_valid_api_key_query_parameter_should_return_ok()
    {
        SetupRequest(apiKeyQuery: ExpectedApiKey);
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_with_valid_access_token_query_parameter_should_return_ok()
    {
        SetupRequest(accessTokenQuery: ExpectedApiKey);
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_with_invalid_api_key_should_return_unauthorized()
    {
        SetupRequest(xApiKeyHeader: "wrong-api-key");
        var payload = new ArrWebhookPayload { EventType = "Grab" };

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<UnauthorizedResult>());
        _webhookService.DidNotReceive().ProcessWebhook(Arg.Any<ArrWebhookPayload>());
    }

    [Test]
    public void ReceiveArrWebhook_with_missing_api_key_when_expected_is_configured_should_return_unauthorized()
    {
        SetupRequest();
        var payload = new ArrWebhookPayload { EventType = "Grab" };

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<UnauthorizedResult>());
        _webhookService.DidNotReceive().ProcessWebhook(Arg.Any<ArrWebhookPayload>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ReceiveArrWebhook_when_expected_api_key_is_null_or_empty_should_allow_request_without_false_rejections(string emptyExpectedKey)
    {
        _configFileProvider.ApiKey.Returns(emptyExpectedKey);
        SetupRequest(xApiKeyHeader: "any-or-no-key");
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }

    [Test]
    public void ReceiveArrWebhook_when_expected_api_key_is_empty_and_no_key_provided_should_allow_request()
    {
        _configFileProvider.ApiKey.Returns(string.Empty);
        SetupRequest();
        var payload = new ArrWebhookPayload { EventType = "Grab" };
        var expectedResult = new ArrWebhookResult { Success = true };
        _webhookService.ProcessWebhook(payload).Returns(expectedResult);

        var response = _controller.ReceiveArrWebhook(payload);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));
    }
}
