using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarbookApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable pg_trgm extension for PostgreSQL to support trigram indexes (safe to run multiple times)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // Gin trigram table for ILIKE substring search
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Posts_Content_Trgm 
                ON ""Posts"" USING GIN (""Content"" gin_trgm_ops);
            ");

            // Gin index for Full Text Search
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Posts_Content_FTS 
                ON ""Posts"" USING GIN (to_tsvector('english', ""Content""));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Posts_Content_Trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Posts_Content_FTS;");
        }
    }
}
