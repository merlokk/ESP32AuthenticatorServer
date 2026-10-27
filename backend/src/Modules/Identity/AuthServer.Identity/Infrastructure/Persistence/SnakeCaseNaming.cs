using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AuthServer.Identity.Infrastructure.Persistence;

/// <summary>PostgreSQL naming for every table, column, key and index: AspNetUsers → asp_net_users.</summary>
internal static class SnakeCaseNaming
{
    public static void UseSnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Owned types (the passkey's JSON data) live in their owner's table.
            if (entity.IsOwned() || entity.GetTableName() is not { } table)
            {
                continue;
            }

            var tableName = ToSnakeCase(table).Replace("open_iddict_", "openiddict_");
            entity.SetTableName(tableName);

            var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName(storeObject) ?? property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];

            if (char.IsUpper(character))
            {
                var startsAWord = index > 0
                    && (!char.IsUpper(name[index - 1]) || (index + 1 < name.Length && char.IsLower(name[index + 1])));

                if (startsAWord && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
