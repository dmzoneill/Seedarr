// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using NUnit.Framework;
using Seedarr.Api.V1.Transmission;

namespace NzbDrone.Core.Test.Transmission;

[TestFixture]
public class TransmissionRpcInputFormatterTest
{
    private TransmissionRpcInputFormatter _formatter;

    [SetUp]
    public void SetUp()
    {
        _formatter = new TransmissionRpcInputFormatter();
    }

    [Test]
    public void Constructor_should_configure_supported_media_types()
    {
        var mediaTypes = _formatter.SupportedMediaTypes;

        Assert.That(mediaTypes, Does.Contain("application/json"));
        Assert.That(mediaTypes, Does.Contain("application/x-www-form-urlencoded"));
        Assert.That(mediaTypes, Does.Contain("text/plain"));
        Assert.That(mediaTypes, Does.Contain("*/*"));
    }

    [Test]
    public void Constructor_should_configure_supported_encodings()
    {
        var encodings = _formatter.SupportedEncodings;

        Assert.That(encodings, Contains.Item(Encoding.UTF8));
        Assert.That(encodings, Contains.Item(Encoding.Unicode));
    }

    [Test]
    public void CanReadType_should_return_true_for_TransmissionRpcRequest()
    {
        var testable = new TestableTransmissionRpcInputFormatter();

        Assert.That(testable.PublicCanReadType(typeof(TransmissionRpcRequest)), Is.True);
    }

    [TestCase(typeof(string))]
    [TestCase(typeof(int))]
    [TestCase(typeof(object))]
    [TestCase(typeof(TransmissionRpcResponse))]
    public void CanReadType_should_return_false_for_non_TransmissionRpcRequest_types(Type type)
    {
        var testable = new TestableTransmissionRpcInputFormatter();

        Assert.That(testable.PublicCanReadType(type), Is.False);
    }

    [Test]
    public void CanRead_should_return_true_for_valid_context()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        var context = CreateContext(stream, modelType: typeof(TransmissionRpcRequest));

        Assert.That(_formatter.CanRead(context), Is.True);
    }

    [Test]
    public void CanRead_should_return_false_for_unsupported_model_type()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        var context = CreateContext(stream, modelType: typeof(string));

        Assert.That(_formatter.CanRead(context), Is.False);
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_deserialize_valid_json_from_seekable_stream()
    {
        var json = "{\"method\": \"torrent-get\", \"arguments\": { \"fields\": [\"id\"] }, \"tag\": 123}";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(stream, contentType: "application/json");

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.EqualTo("torrent-get"));
        Assert.That(request.Arguments, Is.Not.Null);
        Assert.That(request.Arguments.ContainsKey("fields"), Is.True);
        Assert.That(request.Tag.GetInt32(), Is.EqualTo(123));
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_deserialize_valid_json_from_non_seekable_stream()
    {
        var json = "{\"method\": \"torrent-start\", \"arguments\": { \"ids\": [1, 2] }, \"tag\": 456}";
        var innerStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var nonSeekableStream = new NonSeekableStream(innerStream);
        var context = CreateContext(nonSeekableStream, contentType: "application/json");

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.EqualTo("torrent-start"));
        Assert.That(request.Arguments.ContainsKey("ids"), Is.True);
        Assert.That(request.Tag.GetInt32(), Is.EqualTo(456));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\r\n\t")]
    public async Task ReadRequestBodyAsync_should_return_success_with_default_request_for_empty_or_whitespace_body(string body)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var context = CreateContext(stream, contentType: "application/json");

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.Null);
        Assert.That(request.Arguments, Is.Not.Null.And.Empty);
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_extract_json_from_form_url_encoded_content()
    {
        var formFields = new Dictionary<string, StringValues>
        {
            { "{\"method\":\"session-get\"}", string.Empty },
        };
        var formCollection = new FormCollection(formFields);
        var stream = new NonSeekableStream(new MemoryStream());
        var context = CreateContext(stream, contentType: "application/x-www-form-urlencoded", form: formCollection);

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var request = result.Model as TransmissionRpcRequest;
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Method, Is.EqualTo("session-get"));
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_fail_gracefully_on_invalid_json()
    {
        var invalidJson = "{ invalid json content ### }";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
        var context = CreateContext(stream, contentType: "application/json");

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.True);
        Assert.That(context.ModelState.IsValid, Is.False);
        Assert.That(context.ModelState[context.ModelName].Errors, Is.Not.Empty);
    }

    [Test]
    public void ReadRequestBodyAsync_should_throw_on_null_context()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _formatter.ReadRequestBodyAsync(null, Encoding.UTF8));
    }

    [Test]
    public void ReadRequestBodyAsync_should_throw_on_null_encoding()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        var context = CreateContext(stream);

        Assert.ThrowsAsync<ArgumentNullException>(() => _formatter.ReadRequestBodyAsync(context, null));
    }

    private static InputFormatterContext CreateContext(
        Stream bodyStream,
        string contentType = "application/json",
        Type modelType = null,
        IFormCollection form = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = bodyStream;

        if (contentType != null)
        {
            httpContext.Request.ContentType = contentType;
        }

        if (form != null)
        {
            httpContext.Request.Form = form;
        }

        var modelState = new ModelStateDictionary();
        var metadataProvider = new EmptyModelMetadataProvider();
        var metadata = metadataProvider.GetMetadataForType(modelType ?? typeof(TransmissionRpcRequest));

        return new InputFormatterContext(
            httpContext,
            modelName: "request",
            modelState: modelState,
            metadata: metadata,
            readerFactory: (stream, encoding) => new StreamReader(stream, encoding));
    }

    [Test]
    public async Task ReadRequestBodyAsync_should_deserialize_form_with_query_field()
    {
        var json = "{\"method\":\"session-get\",\"tag\":123}";
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "query", json }
        });

        var context = CreateContext(
            new MemoryStream(Encoding.UTF8.GetBytes($"query={Uri.EscapeDataString(json)}")),
            contentType: "application/x-www-form-urlencoded",
            form: form);

        var result = await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.That(result.HasError, Is.False);
        var req = result.Model as TransmissionRpcRequest;
        Assert.That(req, Is.Not.Null);
        Assert.That(req.Method, Is.EqualTo("session-get"));
        Assert.That(req.Tag.GetInt32(), Is.EqualTo(123));
    }

    private class TestableTransmissionRpcInputFormatter : TransmissionRpcInputFormatter
    {
        public bool PublicCanReadType(Type type) => CanReadType(type);
    }

    private class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
