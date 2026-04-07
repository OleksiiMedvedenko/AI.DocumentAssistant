# AI.DocumentAssistant

AI.DocumentAssistant is an ASP.NET Core 8 backend for uploading, processing, analyzing, and querying documents with AI-assisted workflows.

It exposes APIs for:

- user authentication with JWT and refresh tokens,
- document upload and background processing,
- document summarization,
- structured data extraction to JSON,
- comparing two documents,
- chat over a processed document,
- usage tracking and quota enforcement.

The project is organized in a layered architecture and includes integration tests for key end-to-end flows.

---

## Main capabilities

### Authentication
- Register and login
- JWT access tokens
- Refresh token support
- Role-aware backend foundation

### Document workflows
- Upload documents
- Persist document metadata
- Extract text from uploaded files
- Split content into chunks
- Store processed content for later retrieval

### AI features
- Ask questions about a document
- Generate a summary
- Extract structured information into JSON
- Compare two documents

### Operational features
- Background document processing
- Usage / quota tracking
- Integration tests with fake AI provider
- Configurable retrieval behavior

---

## Solution structure

The solution follows a layered architecture:

- `AI.DocumentAssistant.API`
  - HTTP API
  - controllers
  - middleware
  - startup / dependency wiring

- `AI.DocumentAssistant.Application`
  - use cases
  - business services
  - orchestration logic
  - auth, documents, chats, usage, background processing

- `AI.DocumentAssistant.Domain`
  - entities
  - enums
  - core business model

- `AI.DocumentAssistant.Infrastructure`
  - EF Core persistence
  - database access
  - migrations
  - background services
  - external service implementations

- `AI.DocumentAssistant.Abstractions`
  - shared contracts / interfaces

- `AI.DocumentAssistant.UnitTests`
  - integration-style tests for major API flows

---

## Architecture overview

The application is intentionally split so that:

- the API layer handles HTTP concerns,
- the Application layer contains business logic,
- the Domain layer defines the business model,
- the Infrastructure layer implements persistence and integrations.

This keeps controllers thin and reduces coupling between web concerns and business logic.

---

## High-level processing flow

### 1. Authentication
A user authenticates and receives a JWT access token and refresh token.

### 2. Document upload
The client uploads a document through the API.

### 3. Queueing
The document is registered in the database and queued for background processing.

### 4. Background processing
A background worker processes the document:
- reads file content,
- extracts text,
- creates chunks,
- stores extracted content,
- updates processing status.

### 5. AI operations
Once processed, the document can be used for:
- chat,
- summary,
- extraction,
- comparison.

---

## Tech stack

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- SQL Server
- BackgroundService for async processing
- OpenAI integration
- xUnit integration tests

---

## Configuration

The application uses hierarchical ASP.NET Core configuration:

- `appsettings.json`
- `appsettings.Development.json`
- environment variables
- user secrets

### Important recommendation

Do **not** commit secrets to the repository.

Use:
- `dotnet user-secrets`
- environment variables
- a secure secret store / vault in higher environments

---

## Required configuration keys

### Database
- `ConnectionStrings:DefaultConnection`

### JWT
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SecretKey`
- `Jwt:AccessTokenExpirationMinutes`
- `Jwt:RefreshTokenExpirationDays`

### OpenAI
- `OpenAI:ApiKey`
- `OpenAI:Model`
- `OpenAI:BaseUrl`
- `OpenAI:Temperature`

### Optional
- `Database:ApplyMigrationsOnStartup`
- `Cors:AllowedOrigins`
- `ChatRetrieval:*`

---

## Example `appsettings.json`

This file should be safe for the repository and should not contain secrets.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Jwt": {
    "Issuer": "AI.DocumentAssistant",
    "Audience": "AI.DocumentAssistant.Client",
    "SecretKey": "",
    "AccessTokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 7
  },
  "LocalStorage": {
    "RootPath": "storage"
  },
  "OpenAI": {
    "ApiKey": "",
    "Model": "gpt-4o-mini",
    "BaseUrl": "https://api.openai.com/v1/",
    "Temperature": 0.2
  },
  "Database": {
    "ApplyMigrationsOnStartup": false
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173"
    ]
  }
}