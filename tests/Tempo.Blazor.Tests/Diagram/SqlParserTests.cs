using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class SqlParserTests
{
    [Fact]
    public void Parse_ExtractsThreeTablesAndTwoForeignKeys()
    {
        const string sql = @"
            CREATE TABLE Users (
                Id INT PRIMARY KEY,
                Name VARCHAR(255) NOT NULL
            );

            CREATE TABLE Posts (
                Id INT PRIMARY KEY,
                Title VARCHAR(255),
                UserId INT,
                CONSTRAINT FK_Posts_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            CREATE TABLE Comments (
                Id INT PRIMARY KEY,
                Text TEXT,
                PostId INT,
                FOREIGN KEY (PostId) REFERENCES Posts(Id)
            );
        ";

        var tables = SqlParser.Parse(sql);

        tables.Count.Should().Be(3);
        tables.Should().Contain(t => t.Name == "Users");
        tables.Should().Contain(t => t.Name == "Posts");
        tables.Should().Contain(t => t.Name == "Comments");

        var posts = tables.First(t => t.Name == "Posts");
        posts.ForeignKeys.Count.Should().Be(1);
        posts.ForeignKeys[0].ColumnName.Should().Be("UserId");
        posts.ForeignKeys[0].ReferenceTable.Should().Be("Users");
        posts.ForeignKeys[0].ReferenceColumn.Should().Be("Id");

        var comments = tables.First(t => t.Name == "Comments");
        comments.ForeignKeys.Count.Should().Be(1);
        comments.ForeignKeys[0].ColumnName.Should().Be("PostId");
        comments.ForeignKeys[0].ReferenceTable.Should().Be("Posts");
    }

    [Fact]
    public void Parse_ExtractsInlinePrimaryKeyAndForeignKey()
    {
        const string sql = @"
            CREATE TABLE Orders (
                OrderId INT PRIMARY KEY,
                CustomerId INT REFERENCES Customers(CustomerId),
                Total DECIMAL(10,2)
            );
        ";

        var tables = SqlParser.Parse(sql);
        tables.Count.Should().Be(1);

        var orderId = tables[0].Columns.First(c => c.Name == "OrderId");
        orderId.IsPrimaryKey.Should().BeTrue();

        var customerId = tables[0].Columns.First(c => c.Name == "CustomerId");
        customerId.IsForeignKey.Should().BeTrue();
    }

    [Fact]
    public void Parse_HandlesCompositePrimaryKey()
    {
        const string sql = @"
            CREATE TABLE OrderItems (
                OrderId INT,
                ProductId INT,
                Quantity INT,
                CONSTRAINT PK_OrderItems PRIMARY KEY (OrderId, ProductId)
            );
        ";

        var tables = SqlParser.Parse(sql);
        tables.Count.Should().Be(1);
        tables[0].PrimaryKeys.Should().Contain("OrderId");
        tables[0].PrimaryKeys.Should().Contain("ProductId");
    }

    [Fact]
    public void Parse_DetectsSqlServerDialect_WithSquareBrackets()
    {
        const string sql = @"
            CREATE TABLE [dbo].[Products] (
                [ProductID] INT IDENTITY(1,1) PRIMARY KEY,
                [ProductName] NVARCHAR(255) NOT NULL,
                [Price] MONEY
            );
        ";

        var tables = SqlParser.Parse(sql);
        tables.Count.Should().Be(1);
        tables[0].Name.Should().Be("Products");
        tables[0].Dialect.Should().Be(SqlDialect.SqlServer);
        tables[0].Columns.Should().Contain(c => c.Name == "ProductID" && c.DataType == "integer");
        tables[0].Columns.Should().Contain(c => c.Name == "ProductName" && c.DataType == "string");
        tables[0].Columns.Should().Contain(c => c.Name == "Price" && c.DataType == "decimal");
    }

    [Fact]
    public void Parse_DetectsMySqlDialect_WithBackticks()
    {
        const string sql = @"
            CREATE TABLE `categories` (
                `id` INT AUTO_INCREMENT PRIMARY KEY,
                `name` VARCHAR(100) NOT NULL
            ) ENGINE=InnoDB;
        ";

        var tables = SqlParser.Parse(sql);
        tables.Count.Should().Be(1);
        tables[0].Name.Should().Be("categories");
        tables[0].Dialect.Should().Be(SqlDialect.MySql);
    }

    [Fact]
    public void Parse_DetectsPostgreSqlDialect_WithDoubleQuotes()
    {
        const string sql = "CREATE TABLE \"employees\" (\n" +
                           "  \"id\" SERIAL PRIMARY KEY,\n" +
                           "  \"full_name\" VARCHAR(200),\n" +
                           "  \"salary\" NUMERIC(10,2)\n" +
                           ");";

        var tables = SqlParser.Parse(sql);
        tables.Count.Should().Be(1);
        tables[0].Name.Should().Be("employees");
        tables[0].Dialect.Should().Be(SqlDialect.PostgreSql);
    }

    [Fact]
    public void Parse_DetectsJunctionTable_WithCompositePrimaryKey()
    {
        const string sql = @"
            CREATE TABLE StudentCourse (
                StudentId INT,
                CourseId INT,
                EnrollmentDate DATE,
                CONSTRAINT PK_StudentCourse PRIMARY KEY (StudentId, CourseId),
                CONSTRAINT FK_StudentCourse_Student FOREIGN KEY (StudentId) REFERENCES Students(Id),
                CONSTRAINT FK_StudentCourse_Course FOREIGN KEY (CourseId) REFERENCES Courses(Id)
            );

            CREATE TABLE Students (
                Id INT PRIMARY KEY,
                Name VARCHAR(100)
            );

            CREATE TABLE Courses (
                Id INT PRIMARY KEY,
                Title VARCHAR(100)
            );
        ";

        var tables = SqlParser.Parse(sql);
        var junction = tables.First(t => t.Name == "StudentCourse");
        junction.IsJunctionTable.Should().BeTrue();
        junction.ForeignKeys.Count.Should().Be(2);

        var students = tables.First(t => t.Name == "Students");
        students.IsJunctionTable.Should().BeFalse();
    }

    [Fact]
    public void Parse_MapsDataTypes_ToErNotation()
    {
        const string sql = @"
            CREATE TABLE TypeTable (
                A INT,
                B BIGINT,
                C VARCHAR(10),
                D NVARCHAR(10),
                E TEXT,
                F DECIMAL(10,2),
                G MONEY,
                H FLOAT,
                I BIT,
                J DATETIME,
                K UUID,
                L BINARY(16)
            );
        ";

        var table = SqlParser.Parse(sql).First();
        table.Columns.First(c => c.Name == "A").DataType.Should().Be("integer");
        table.Columns.First(c => c.Name == "B").DataType.Should().Be("integer");
        table.Columns.First(c => c.Name == "C").DataType.Should().Be("string");
        table.Columns.First(c => c.Name == "D").DataType.Should().Be("string");
        table.Columns.First(c => c.Name == "E").DataType.Should().Be("string");
        table.Columns.First(c => c.Name == "F").DataType.Should().Be("decimal");
        table.Columns.First(c => c.Name == "G").DataType.Should().Be("decimal");
        table.Columns.First(c => c.Name == "H").DataType.Should().Be("float");
        table.Columns.First(c => c.Name == "I").DataType.Should().Be("boolean");
        table.Columns.First(c => c.Name == "J").DataType.Should().Be("datetime");
        table.Columns.First(c => c.Name == "K").DataType.Should().Be("uuid");
        table.Columns.First(c => c.Name == "L").DataType.Should().Be("binary");
    }

    [Fact]
    public void Parse_HandlesRealWorldMySqlDump()
    {
        const string sql = @"
CREATE TABLE `users` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `email` varchar(255) NOT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `email` (`email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `roles` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(50) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `user_roles` (
  `user_id` int(11) NOT NULL,
  `role_id` int(11) NOT NULL,
  PRIMARY KEY (`user_id`,`role_id`),
  KEY `role_id` (`role_id`),
  CONSTRAINT `user_roles_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`),
  CONSTRAINT `user_roles_ibfk_2` FOREIGN KEY (`role_id`) REFERENCES `roles` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
";

        var tables = SqlParser.Parse(sql);
        tables.Count.Should().Be(3);

        var users = tables.First(t => t.Name == "users");
        users.Columns.Should().Contain(c => c.Name == "id" && c.IsPrimaryKey);
        users.Columns.Should().Contain(c => c.Name == "email" && c.DataType == "string");

        var userRoles = tables.First(t => t.Name == "user_roles");
        userRoles.IsJunctionTable.Should().BeTrue();
        userRoles.ForeignKeys.Should().Contain(fk => fk.ReferenceTable == "users");
        userRoles.ForeignKeys.Should().Contain(fk => fk.ReferenceTable == "roles");
    }

    [Fact]
    public void Parse_PathologicalInput_ThrowsInputTooComplex_OrCompletesQuickly()
    {
        // One never-closing CREATE TABLE repeated 20k times: the single-line body pattern must
        // either finish fast or give up through the typed InputTooComplex error — never hang.
        var sql = string.Concat(Enumerable.Repeat("CREATE TABLE t ( a INT ", 20000));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _ = SqlParser.Parse(sql);
        }
        catch (DiagramImportException ex)
        {
            ex.Error.Should().Be(DiagramImportError.InputTooComplex);
            return;
        }
        finally
        {
            sw.Stop();
        }

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "the parser must stay within the time budget on a pathological input");
    }

    [Fact]
    public void Parse_InputOver512Kb_ThrowsInputTooComplex()
    {
        // The up-front length cap rejects an oversized script without running any regex.
        var sql = "CREATE TABLE t (a INT); " + new string('x', 512 * 1024);

        var act = () => SqlParser.Parse(sql);
        act.Should().Throw<DiagramImportException>()
            .Which.Error.Should().Be(DiagramImportError.InputTooComplex);
    }

    [Fact]
    public void Parse_ExhaustedOverallBudget_ThrowsInputTooComplex_BetweenMatches()
    {
        // Many tables, each cheap: the per-match regex never times out, but the overall budget
        // (checked between matches and column lines) still gives up. The internal overload takes
        // the budget so a test need not burn a real second.
        var sql = string.Concat(Enumerable.Range(0, 200)
            .Select(i => $"CREATE TABLE Table{i} (Id INT PRIMARY KEY, Name VARCHAR(50));"));

        SqlParser.Parse(sql, TimeSpan.FromMinutes(1)).Count.Should().Be(200);

        var act = () => SqlParser.Parse(sql, TimeSpan.Zero);
        act.Should().Throw<DiagramImportException>()
            .Which.Error.Should().Be(DiagramImportError.InputTooComplex);
    }
}
