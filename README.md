# Church Management System API

A professional and robust backend API built with .NET 8, designed to streamline church operations, student management, and attendance tracking. This system provides a comprehensive suite of tools for managing church services, student records, and visitation logs.

## 🚀 Key Features

- **Student Management**: Full CRUD operations for student records, including personal details, grades, and QR code integration.
- **Attendance Tracking**: Efficiently track attendance for various church services with status updates (Present, Absent, Excused) and comments.
- **Church & Service Management**: Manage different church branches and the services they offer.
- **Subscription & Visitation**: Track student subscriptions and log visitation details to ensure consistent engagement.
- **User Authentication**: Secure access using JWT (JSON Web Token) bearer authentication.
- **Automated Logging**: Integrated system for tracking deleted student records for auditing purposes.
- **API Documentation**: Interactive API exploration and testing via Swagger/OpenAPI.

## 🛠 Tech Stack

- **Framework**: [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Language**: C#
- **Database**: SQL Server
- **ORM**: Entity Framework Core 9.0
- **Documentation**: Swagger (Swashbuckle)
- **Authentication**: JWT Bearer

## 🏁 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (Express or Developer edition)
- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) or [VS Code](https://code.visualstudio.com/)

### Installation

1. **Clone the repository**:
   ```bash
   git clone https://github.com/your-username/church-management-api.git
   cd church-management-api
   ```

2. **Configure the Database**:
   Update the connection string in `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "con": "Server=YOUR_SERVER;Database=ChurchDb;Trusted_Connection=True;TrustServerCertificate=True;"
   }
   ```

3. **Apply Migrations**:
   Run the following command to create the database schema:
   ```bash
   dotnet ef database update
   ```

4. **Run the Application**:
   ```bash
   dotnet run
   ```

## 📖 API Documentation

Once the application is running, you can access the interactive Swagger documentation at:
`https://localhost:7112/swagger/index.html` (port may vary based on your configuration).

## 📁 Project Structure

- `Controllers/`: API endpoints for Students, Attendance, Churches, etc.
- `Models/`: Data entities and Data Transfer Objects (DTOs).
- `Migrations/`: Database schema version control.
- `Program.cs`: Application startup and service configuration.

## 🛡 Authentication

The API uses JWT Bearer tokens for secure access. To authorize requests:
1. Obtain a token via the `Users` authentication endpoint.
2. Include the token in the `Authorization` header as: `Bearer {your_token}`.

