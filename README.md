# LLM Council — C# Port

![llmcouncil](header.jpg)

This branch contains a **C# / ASP.NET Core** port of the LLM Council backend.
The React + Vite frontend is unchanged — only the Python backend has been replaced.

## What is LLM Council?

Instead of asking a single LLM your question, LLM Council groups multiple models into a "council". Your query goes through three stages:

1. **Stage 1 – First opinions.** All council models answer independently. Their individual responses are shown in a tab view.
2. **Stage 2 – Peer review.** Each model evaluates the other models' answers. Identities are anonymised to prevent bias. Models rank responses from best to worst.
3. **Stage 3 – Final synthesis.** A designated "Chairman" model reads all responses and rankings, then produces a single, comprehensive answer.

## Architecture

| Layer | Technology |
|---|---|
| Backend API | ASP.NET Core 10 (`backend-csharp/`) |
| LLM access | [OpenRouter](https://openrouter.ai/) or local [LiteLLM](https://docs.litellm.ai/) |
| Storage | JSON files in `data/conversations/` |
| Frontend | React + Vite (unchanged, in `frontend/`) |

### Backend project structure

```
backend-csharp/
├── LlmCouncil/
│   ├── Controllers/
│   │   └── ConversationsController.cs   # All REST endpoints + SSE streaming
│   ├── Models/
│   │   └── CouncilModels.cs             # Request/response/storage models
│   ├── Services/
│   │   ├── CouncilOptions.cs            # Typed configuration
│   │   ├── OpenRouterService.cs         # HTTP client for OpenRouter / LiteLLM
│   │   ├── CouncilService.cs            # 3-stage orchestration logic
│   │   └── StorageService.cs            # JSON file persistence
│   ├── Program.cs                       # DI wiring + CORS + middleware
│   └── appsettings.json                 # Default configuration
└── LlmCouncil.Tests/
    ├── CouncilServiceTests.cs           # Unit tests for ranking logic
    └── StorageServiceTests.cs           # Unit tests for file storage
```

## Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/) (for the frontend)
- Either an [OpenRouter](https://openrouter.ai/) API key or a local LiteLLM endpoint

### 1. Install frontend dependencies

```bash
cd frontend
npm install
cd ..
```

### 2. Configure your provider

Create a `.env` file in the **project root** (same location as the original Python version):

```bash
# OpenRouter mode (default)
OPENROUTER_API_KEY=sk-or-v1-...

# LiteLLM mode (optional)
LLM_PROVIDER=LiteLLM
LITELLM_API_URL=http://localhost:4000/v1/chat/completions
# Optional if your LiteLLM server requires auth:
# LITELLM_API_KEY=your-key

# Council model configuration (comma-separated list)
COUNCIL_MODELS=openai/gpt-5.1,google/gemini-3-pro-preview,anthropic/claude-sonnet-4.5,x-ai/grok-4
CHAIRMAN_MODEL=google/gemini-3-pro-preview
```

You can also configure provider settings via `appsettings.json`:

```json
{
  "Council": {
    "LlmProvider": "OpenRouter",
    "OpenRouterApiKey": "sk-or-v1-...",
    "LiteLlmApiUrl": "http://localhost:4000/v1/chat/completions",
    "LiteLlmApiKey": ""
  }
}
```

### 3. Configure models

Set `COUNCIL_MODELS` and `CHAIRMAN_MODEL` in `.env`.

## Running the application

**Terminal 1 — Backend:**

```bash
cd backend-csharp/LlmCouncil
dotnet run
```

The API starts on **http://localhost:8001** (same port as the Python version).

**Terminal 2 — Frontend:**

```bash
cd frontend
npm run dev
```

Then open **http://localhost:5173** in your browser.

## Running the tests

```bash
cd backend-csharp
dotnet test
```

All 19 unit tests cover:
- `CouncilService.ParseRankingFromText` — "FINAL RANKING:" section extraction and fallbacks
- `CouncilService.CalculateAggregateRankings` — average rank computation with agreement and disagreement cases
- `StorageService` — conversation CRUD, message appending, title updates, listing order

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | Health check |
| GET | `/api/conversations` | List all conversations |
| POST | `/api/conversations` | Create a new conversation |
| GET | `/api/conversations/{id}` | Get a specific conversation |
| POST | `/api/conversations/{id}/message` | Send a message (batch response) |
| POST | `/api/conversations/{id}/message/stream` | Send a message (SSE streaming) |

## Tech stack

- **Backend:** ASP.NET Core 10, `System.Net.Http.HttpClient`, `System.Text.Json`
- **Frontend:** React + Vite, react-markdown (unchanged from original)
- **Storage:** JSON files in `data/conversations/`
- **Package management:** `dotnet` CLI / NuGet for C#, npm for the frontend

## Differences from the Python version

| Feature | Python | C# |
|---------|--------|----|
| Framework | FastAPI | ASP.NET Core |
| Async HTTP | httpx | `HttpClient` + `Task.WhenAll` |
| JSON | pydantic / json | `System.Text.Json` |
| Config | `config.py` + `.env` | `appsettings.json` + env vars |
| DI | manual | ASP.NET Core DI container |
| Tests | none | xUnit (18 unit tests) |
| Port | 8001 | 8001 (identical) |
