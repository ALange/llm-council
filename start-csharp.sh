#!/usr/bin/env bash
# start-csharp.sh – launch the C# backend and React frontend together
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"

# Backend
echo "Starting C# backend on http://localhost:8001 ..."
(cd "$ROOT/backend-csharp/LlmCouncil" && dotnet run) &
BACKEND_PID=$!

# Frontend
echo "Starting React frontend on http://localhost:5173 ..."
(cd "$ROOT/frontend" && npm run dev) &
FRONTEND_PID=$!

echo ""
echo "  Backend PID : $BACKEND_PID"
echo "  Frontend PID: $FRONTEND_PID"
echo ""
echo "  Open http://localhost:5173 in your browser."
echo "  Press Ctrl+C to stop both servers."

# Wait for both and forward SIGINT
trap "kill $BACKEND_PID $FRONTEND_PID 2>/dev/null; exit 0" INT TERM
wait $BACKEND_PID $FRONTEND_PID
