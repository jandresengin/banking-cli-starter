#!/bin/sh
set -eu

# Containers in the same Container Apps replica share localhost.
export OLLAMA_HOST="${OLLAMA_HOST:-127.0.0.1:11434}"
model="${OLLAMA_MODEL:-gemma3:1b}"
ollama serve &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; exit 0' TERM INT

until ollama list >/dev/null 2>&1; do
    if ! kill -0 "$server_pid" 2>/dev/null; then
        echo 'Ollama failed to start.' >&2
        wait "$server_pid"
        exit 1
    fi
    sleep 2
done

# The Azure Files mount keeps the downloaded model between replica restarts.
if ! ollama show "$model" >/dev/null 2>&1; then
    until ollama pull "$model"; do
        if ! kill -0 "$server_pid" 2>/dev/null; then
            echo 'Ollama stopped while downloading the model.' >&2
            exit 1
        fi
        echo 'Model download failed; retrying in 30 seconds.' >&2
        sleep 30
    done
fi
echo "Model $model is available."
wait "$server_pid"
