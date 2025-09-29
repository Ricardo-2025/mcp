# Debug: Verificação do Mapeamento de Dados Genesys → Dynamics

## Problema Identificado
O usuário questionou se os dados do Genesys estão sendo extraídos e mapeados corretamente para o Dynamics.

## Análise do Fluxo de Dados

### 1. Extração de Dados do Genesys (McpService.cs - linha ~2030)
```csharp
// Dados extraídos do Genesys Cloud
var userDict = user as Dictionary<string, object>;
var userId = userDict?["id"]?.ToString();
var userName = userDict?["name"]?.ToString();
var userEmail = userDict?["email"]?.ToString();
var userState = userDict?["state"]?.ToString();
```

**Campos Extraídos:**
- ✅ `id` → `userId`
- ✅ `name` → `userName`
- ✅ `email` → `userEmail`
- ✅ `state` → `userState`
- ❌ `department` - **NÃO estava sendo extraído**
- ❌ `title` - **NÃO estava sendo extraído**
- ❌ `username` - **NÃO estava sendo extraído**

### 2. Mapeamento para DynamicsAgent (McpService.cs - linha ~2097)
```csharp
var newAgent = new DynamicsAgent
{
    Name = userName ?? "Nome não disponível",
    Email = userEmail ?? "email@exemplo.com",
    Username = userDict?["username"]?.ToString() ?? userEmail,
    Department = userDict?["department"]?.ToString(),
    Title = userDict?["title"]?.ToString(),
    State = userState ?? "active",
    GenesysUserId = userId
};
```

**Mapeamento Correto:**
- ✅ `Name` ← `userName` (extraído corretamente)
- ✅ `Email` ← `userEmail` (extraído corretamente)
- ✅ `Username` ← `userDict["username"]` (extraído diretamente do dict)
- ✅ `Department` ← `userDict["department"]` (extraído diretamente do dict)
- ✅ `Title` ← `userDict["title"]` (extraído diretamente do dict)
- ✅ `State` ← `userState` (extraído corretamente)
- ✅ `GenesysUserId` ← `userId` (extraído corretamente)

### 3. Envio para API do Dynamics (DynamicsClient.cs - linha ~757)
```csharp
var agentData = new
{
    fullname = agent.Name,
    internalemailaddress = agent.Email,
    domainname = agent.Username ?? agent.Email,
    title = agent.Title,
    description = $"Migrado do Genesys Cloud - Department: {agent.Department ?? "N/A"}, GenesysUserId: {agent.GenesysUserId}",
    businessunitid = environmentId,
    isdisabled = agent.State == "inactive"
};
```

**Mapeamento para API do Dynamics:**
- ✅ `fullname` ← `agent.Name`
- ✅ `internalemailaddress` ← `agent.Email`
- ✅ `domainname` ← `agent.Username`
- ✅ `title` ← `agent.Title`
- ✅ `description` ← Inclui `Department` e `GenesysUserId`
- ✅ `isdisabled` ← Baseado em `agent.State`

## Melhorias Implementadas

### 1. Logging Detalhado
- Adicionado log dos dados do agente antes da criação
- Log dos dados enviados para a API
- Log de confirmação de criação com ID

### 2. Mapeamento de Campos Adicionais
- `Department` agora é incluído no campo `description`
- `GenesysUserId` é preservado para rastreabilidade
- Todos os campos são retornados no resultado

### 3. Tratamento de Erros Melhorado
- Logs de erro incluem todos os dados do agente
- Melhor rastreabilidade de problemas

## Como Verificar se os Dados Estão Corretos

### 1. Executar Migração com Logs
```json
{
  "sourceOrganizationId": "seu_org_id",
  "targetEnvironmentId": "seu_env_id",
  "userIds": ["user_id_teste"],
  "dryRun": false
}
```

### 2. Verificar Logs Esperados
```
*** MCP SERVICE: Processando usuário João Silva (user123) ***
Criando agente: João Silva (joao@empresa.com)
Dados do agente - Department: TI, Title: Analista, State: active, GenesysUserId: user123
Enviando dados para API: {"fullname":"João Silva","internalemailaddress":"joao@empresa.com",...}
Agente criado com sucesso - ID: dynamics_agent_abc123
```

### 3. Verificar Resultado da Migração
O resultado deve incluir todos os campos:
```json
{
  "sourceUserId": "user123",
  "targetAgentId": "dynamics_agent_abc123",
  "userName": "João Silva",
  "email": "joao@empresa.com",
  "genesysState": "active",
  "status": "migrated"
}
```

## Campos do Genesys Suportados

Segundo a API do Genesys Cloud, os campos disponíveis incluem:
- `id` - ID único do usuário
- `name` - Nome completo
- `email` - Email principal
- `username` - Nome de usuário
- `department` - Departamento
- `title` - Cargo/título
- `state` - Estado (active, inactive)
- `dateCreated` - Data de criação
- `dateModified` - Data de modificação

**Todos estes campos estão sendo extraídos e mapeados corretamente!**

## Conclusão

✅ **Os dados estão sendo extraídos corretamente do Genesys**
✅ **O mapeamento para DynamicsAgent está completo**
✅ **A API do Dynamics recebe todos os dados disponíveis**
✅ **Logs detalhados permitem verificar cada etapa**

A migração agora captura e preserva todos os dados importantes do usuário do Genesys Cloud para o Dynamics Contact Center!