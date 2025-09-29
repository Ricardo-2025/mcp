# Sistema de Gerenciamento de Sessões MCP

## Problema Resolvido

O sistema anterior utilizava um `Dictionary` estático para armazenar sessões, o que causava perda de sessões entre requisições em ambientes Azure Functions devido ao cold start e compartilhamento de instâncias.

## Solução Implementada

### 1. Serviço de Sessões Thread-Safe

- **ISessionService**: Interface para gerenciamento de sessões
- **SessionService**: Implementação thread-safe usando `ConcurrentDictionary`
- **SessionConfiguration**: Configurações personalizáveis para timeout e limpeza

### 2. Recursos Principais

#### Gerenciamento Automático
- **Criação**: Sessões são criadas automaticamente quando necessário
- **Validação**: Verificação de existência e expiração de sessões
- **Limpeza**: Timer automático remove sessões expiradas
- **Thread-Safety**: Operações seguras em ambiente multi-thread

#### Novos Métodos MCP
- `session/info`: Retorna informações detalhadas da sessão atual
- `session/list`: Lista todas as sessões ativas (útil para debug)
- `session/terminate`: Termina explicitamente uma sessão

### 3. Configuração

#### appsettings.json
```json
{
  "Session": {
    "SessionTimeoutMinutes": 30,    // Timeout da sessão
    "CleanupIntervalMinutes": 10,   // Intervalo de limpeza automática
    "MaxConcurrentSessions": 1000   // Limite de sessões simultâneas
  }
}
```

### 4. Fluxo de Funcionamento

1. **Initialize**: Cliente chama `initialize` e recebe session ID
2. **Notification**: Cliente envia `notifications/initialized`
3. **Operations**: Todas as operações subsequentes usam o session ID
4. **Validation**: Cada requisição valida a sessão automaticamente
5. **Cleanup**: Sessões expiradas são removidas automaticamente
6. **Termination**: Sessão pode ser terminada explicitamente ou por timeout

### 5. Logs e Monitoramento

O sistema registra logs detalhados para:
- Criação de novas sessões
- Validação de sessões
- Remoção de sessões expiradas
- Tentativas de acesso a sessões inválidas
- Estatísticas de limpeza automática

### 6. Benefícios

- **Persistência**: Sessões mantidas corretamente entre requisições
- **Performance**: Limpeza automática evita vazamentos de memória
- **Observabilidade**: Logs detalhados para debug e monitoramento
- **Flexibilidade**: Configurações personalizáveis por ambiente
- **Robustez**: Tratamento adequado de cenários edge cases

### 7. Compatibilidade

- Mantém compatibilidade total com protocolo MCP existente
- Headers `Mcp-Session-Id` preservados
- Comportamento de notificações inalterado
- Métodos existentes funcionam normalmente

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

# 2. Usar sessão
response = requests.post(url, json={
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {"name": "process_question", "arguments": {"question": "test"}}
}, headers={"Mcp-Session-Id": session_id})

# A sessão será mantida entre todas as requisições!
```

## Implementação Técnica

- **Dependency Injection**: SessionService registrado como Singleton
- **Concurrent Collections**: ConcurrentDictionary para thread-safety
- **Timer**: Limpeza automática com Timer.NET
- **Configuration**: IOptions pattern para configurações
- **Logging**: ILogger integrado para observabilidade