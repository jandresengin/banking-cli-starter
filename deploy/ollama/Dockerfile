FROM ollama/ollama:0.40.16
COPY deploy/ollama/start.sh /usr/local/bin/start-banking-ollama
RUN chmod 755 /usr/local/bin/start-banking-ollama
ENV OLLAMA_HOST=127.0.0.1:11434 OLLAMA_MODEL=gemma3:1b
ENTRYPOINT ["/usr/local/bin/start-banking-ollama"]
