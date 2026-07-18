using MySql.Data.MySqlClient;

namespace OnlineClearanceSystem.Data
{
    public static class DbHelper
    {
        public static string GetConnectionString(IConfiguration config)
        {
            var dbHost = config["DB_HOST"];
            if (!string.IsNullOrWhiteSpace(dbHost))
            {
                var dbPort = config["DB_PORT"] ?? "3306";
                var dbName = config["DB_NAME"] ?? "schoolclearance_db";
                var dbUser = config["DB_USER"] ?? "schoolclearance";
                var dbPassword = config["DB_PASSWORD"] ?? string.Empty;
                return $"server={dbHost};port={dbPort};database={dbName};user={dbUser};password={dbPassword};";
            }

            var connectionString = config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Configure ConnectionStrings:DefaultConnection or the DB_* environment variables.");

            return connectionString;
        }

        public static MySqlConnection GetConnection(IConfiguration config)
        {
            return new MySqlConnection(GetConnectionString(config));
        }
    }
}
