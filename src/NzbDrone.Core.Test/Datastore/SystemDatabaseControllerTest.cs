// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class SystemDatabaseControllerTest
{
    private SqliteConnection _sqliteConnection;
    private IMainDatabase _mainDatabase;
    private SystemDatabaseController _controller;

    [SetUp]
    public void SetUp()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();

        using (var cmd = _sqliteConnection.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Torrents (Id INTEGER PRIMARY KEY, Name TEXT, Size INT); " +
                "CREATE INDEX IX_Torrents_Name ON Torrents(Name); " +
                "INSERT INTO Torrents (Name, Size) VALUES ('TestTorrent', 1024); " +
                "CREATE TABLE Trackers (Id INTEGER PRIMARY KEY, TorrentId INT REFERENCES Torrents(Id), Url TEXT); " +
                "INSERT INTO Trackers (TorrentId, Url) VALUES (1, 'udp://tracker.example.com'); " +
                "CREATE TABLE Metrics (Id INTEGER PRIMARY KEY, Timestamp DATETIME, Active BOOL, Speed REAL, Hash BLOB, Count BIGINT); " +
                "INSERT INTO Metrics (Timestamp, Active, Speed, Count) VALUES (CURRENT_TIMESTAMP, 1, 12.5, 9999999999);";
            cmd.ExecuteNonQuery();
        }

        _mainDatabase = Substitute.For<IMainDatabase>();
        _mainDatabase.OpenConnection().Returns(_ => _sqliteConnection);

        _controller = new SystemDatabaseController(_mainDatabase);
    }

    [TearDown]
    public void TearDown()
    {
        _sqliteConnection?.Dispose();
    }

    [Test]
    public void GetTables_should_return_list_of_tables_with_counts()
    {
        var response = _controller.GetTables();
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var tables = okResult.Value as List<DatabaseTableResource>;
        Assert.That(tables, Is.Not.Null);
        Assert.That(tables.Count, Is.GreaterThanOrEqualTo(1));
        var torrentsTable = tables.Find(t => t.Name == "Torrents");
        Assert.That(torrentsTable, Is.Not.Null);
        Assert.That(torrentsTable.RowCount, Is.EqualTo(1));
        Assert.That(torrentsTable.ColumnCount, Is.EqualTo(3));
    }

    [Test]
    public void GetSchema_should_return_table_schemas_and_mermaid_erd()
    {
        var response = _controller.GetSchema();
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var schema = okResult.Value as DatabaseSchemaResponse;
        Assert.That(schema, Is.Not.Null);
        Assert.That(schema.Tables.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(schema.MermaidErd, Does.Contain("erDiagram"));
    }

    [Test]
    public void GetStorage_should_return_storage_metrics()
    {
        var response = _controller.GetStorage();
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var storage = okResult.Value as DatabaseStorageResponse;
        Assert.That(storage, Is.Not.Null);
        Assert.That(storage.PageSize, Is.GreaterThan(0));
    }

    [Test]
    public async Task ExecuteQuery_should_reject_empty_query()
    {
        var response = await _controller.ExecuteQuery(new DatabaseQueryRequest { Query = "" });
        var badRequest = response.Result as BadRequestObjectResult;

        Assert.That(badRequest, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteQuery_should_reject_write_query_in_safe_mode()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "DELETE FROM Torrents WHERE Id = 1",
            ReadOnly = true,
        };

        var response = await _controller.ExecuteQuery(request);
        var badRequest = response.Result as BadRequestObjectResult;

        Assert.That(badRequest, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteQuery_should_execute_read_query_successfully()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "SELECT Id, Name, Size FROM Torrents",
            ReadOnly = true,
        };

        var response = await _controller.ExecuteQuery(request);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var queryResult = okResult.Value as DatabaseQueryResult;
        Assert.That(queryResult, Is.Not.Null);
        Assert.That(queryResult.Success, Is.True);
        Assert.That(queryResult.Columns.Count, Is.EqualTo(3));
        Assert.That(queryResult.Rows.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task ExecuteQuery_should_execute_write_query_when_readonly_is_false()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "INSERT INTO Torrents (Name, Size) VALUES ('NewTorrent', 2048)",
            ReadOnly = false,
        };

        var response = await _controller.ExecuteQuery(request);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var queryResult = okResult.Value as DatabaseQueryResult;
        Assert.That(queryResult, Is.Not.Null);
        Assert.That(queryResult.Success, Is.True);
        Assert.That(queryResult.RowsAffected, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteQuery_should_handle_syntax_errors_gracefully()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "SELECT INVALID SYNTAX FROM",
            ReadOnly = true,
        };

        var response = await _controller.ExecuteQuery(request);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var result = okResult.Value as DatabaseQueryResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteQuery_should_support_pagination()
    {
        var request = new DatabaseQueryRequest
        {
            Query = "SELECT Id, Name FROM Torrents LIMIT 1 OFFSET 0",
            ReadOnly = true,
        };

        var response = await _controller.ExecuteQuery(request);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
    }

    [Test]
    public void Controller_should_have_Authorize_AdminOnly_attribute()
    {
        var type = typeof(SystemDatabaseController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
    }

    [TestCase("/* comment */ DELETE FROM Torrents")]
    [TestCase("-- comment\nDELETE FROM Torrents")]
    [TestCase("SELECT 1; DROP TABLE Torrents;")]
    [TestCase("SELECT 1; DELETE FROM Torrents;")]
    public async Task ExecuteQuery_should_reject_bypass_attempts_in_safe_mode(string query)
    {
        var request = new DatabaseQueryRequest
        {
            Query = query,
            ReadOnly = true
        };

        var response = await _controller.ExecuteQuery(request);
        var badRequest = response.Result as BadRequestObjectResult;

        Assert.That(badRequest, Is.Not.Null);
        var result = badRequest.Value as DatabaseQueryResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
    }
}
