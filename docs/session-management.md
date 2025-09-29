# Sistema de Gerenciamento de Sess�es MCP

## Problema Resolvido

O sistema anterior utilizava um `Dictionary` est�tico para armazenar sess�es, o que causava perda de sess�es entre requisi��es em ambientes Azure Functions devido ao cold start e compartilhamento de inst�ncias.

## Solu��o Implementada

### 1. Servi�o de Sess�es Thread-Safe

- **ISessionService**: Interface para gerenciamento de sess�es
- **SessionService**: Implementa��o thread-safe usando `ConcurrentDictionary`
- **SessionConfiguration**: Configura��es personaliz�veis para timeout e limpeza

### 2. Recursos Principais

#### Gerenciamento Autom�tico
- **Cria��o**: Sess�es s�o criadas automaticamente quando necess�rio
- **Valida��o**: Verifica��o de exist�ncia e expira��o de sess�es
- **Limpeza**: Timer autom�tico remove sess�es expiradas
- **Thread-Safety**: Opera��es seguras em ambiente multi-thread

#### Novos M�todos MCP
- `session/info`: Retorna informa��es detalhadas da sess�o atual
- `session/list`: Lista todas as sess�es ativas (�til para debug)
- `session/terminate`: Termina explicitamente uma sess�o

### 3. Configura��o

#### appsettings.json
```json
{
  "Session": {
    "SessionTimeoutMinutes": 30,    // Timeout da sess�o
    "CleanupIntervalMinutes": 10,   // Intervalo de limpeza autom�tica
    "MaxConcurrentSessions": 1000   // Limite de sess�es simult�neas
  }
}
```

### 4. Fluxo de Funcionamento

1. **Initialize**: Cliente chama `initialize` e recebe session ID
2. **Notification**: Cliente envia `notifications/initialized`
3. **Operations**: Todas as opera��es subsequentes usam o session ID
4. **Validation**: Cada requisi��o valida a sess�o automaticamente
5. **Cleanup**: Sess�es expiradas s�o removidas automaticamente
6. **Termination**: Sess�o pode ser terminada explicitamente ou por timeout

### 5. Logs e Monitoramento

O sistema registra logs detalhados para:
- Cria��o de novas sess�es
- Valida��o de sess�es
- Remo��o de sess�es expiradas
- Tentativas de acesso a sess�es inv�lidas
- Estat�sticas de limpeza autom�tica

### 6. Benef�cios

- **Persist�ncia**: Sess�es mantidas corretamente entre requisi��es
- **Performance**: Limpeza autom�tica evita vazamentos de mem�ria
- **Observabilidade**: Logs detalhados para debug e monitoramento
- **Flexibilidade**: Configura��es personaliz�veis por ambiente
- **Robustez**: Tratamento adequado de cen�rios edge cases

### 7. Compatibilidade

- Mant�m compatibilidade total com protocolo MCP existente
- Headers `Mcp-Session-Id` preservados
- Comportamento de notifica��es inalterado
- M�todos existentes funcionam normalmente

### 8. Exemplo de Uso

```python
# 1. Inicializar
response = requests.post(url, json={
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {"protocolVersion": "2025-03-26"}
})

session_id = response.headers.get("Mcp-Session-Id")

# 2. Usar sess�o
response = requests.post(url, json={
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {"name": "process_question", "arguments": {"question": "test"}}
}, headers={"Mcp-Session-Id": session_id})

# A sess�o ser� mantida entre todas as requisi��es!
```

## Implementa��o T�cnica

- **Dependency Injection**: SessionService registrado como Singleton
- **Concurrent Collections**: ConcurrentDictionary para thread-safety
- **Timer**: Limpeza autom�tica com Timer.NET
- **Configuration**: IOptions pattern para configura��es
- **Logging**: ILogger integrado para observabilidade