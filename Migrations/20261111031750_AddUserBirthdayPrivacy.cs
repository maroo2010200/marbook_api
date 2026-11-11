using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarbookApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBirthdayPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBirthdatePrivate",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBirthdatePrivate",
                table: "Users");
        }
    }
}
