# Correção do Erro de Campo 'description' na Entidade systemuser

## Problemas Identificados

### Erro 1: Campo 'description' Inválido
```
Error identified in Payload provided by the user for Entity :'systemusers'
Invalid property 'description' was found in entity 'Microsoft.Dynamics.CRM.systemuser'
The property 'description' does not exist on type 'Microsoft.Dynamics.CRM.systemuser'
```

### Erro 2: Campo 'businessunitid' Inválido
```
Unable to retrieve attribute=businessunitid for entityLogicalName=systemuser
Entity has Attribute Count=37
```

## Causa dos Erros

O método `CreateAgentAsync` no `DynamicsClient.cs` estava tentando mapear dados para campos que não existem na entidade `systemuser` do Dynamics 365:
- Campo `description`: Não existe na entidade systemuser
- Campo `businessunitid`: Não existe na entidade systemuser (apenas 37 atributos disponíveis)

## Correções Implementadas

### 1. Remoção de Campos Inválidos
- Removido o campo `description` do mapeamento de dados
- Removido o campo `businessunitid` do mapeamento de dados

### 2. Mapeamento Corrigido
- **GenesysUserId**: Mapeado para o campo `employeeid` (campo padrão da entidade systemuser)
- **Title**: Corrigido de `title` para `jobtitle` (nome correto do campo na entidade)
- **Department**: Removido temporariamente (será implementado via businessunitid)

### 3. Campos Finais Utilizados (apenas campos básicos válidos)
- `fullname`: Nome completo do usuário
- `internalemailaddress`: Email interno
- `domainname`: Nome de domínio/username
- `jobtitle`: Título/cargo (campo correto)
- `employeeid`: ID do usuário no Genesys
- `isdisabled`: Status ativo/inativo

**Nota**: Campos como `Department` precisarão ser tratados de forma diferente, possivelmente através de relacionamentos ou campos customizados.

## Código Corrigido

```csharp
// Código corrigido - usando apenas campos básicos que existem na entidade systemuser
// Não incluir businessunitid pois não existe na entidade
var agentData = new
{
    fullname = agent.Name,
    internalemailaddress = agent.Email,
    domainname = agent.Username ?? agent.Email,
    jobtitle = agent.Title,
    employeeid = agent.GenesysUserId,
    isdisabled = agent.State == "inactive"
};
```

## Como Testar

### 1. Executar Migração
```bash
# No terminal do projeto
dotnet build
# Verificar se build é bem-sucedido
```

### 2. Testar Criação de Usuário
```json
{
  "tool": "migrate_users",
  "arguments": {
    "sourceEnvironmentId": "seu-genesys-env-id",
    "targetEnvironmentId": "seu-dynamics-env-id",
    "userIds": ["user-id-para-testar"]
  }
}
```

### 3. Verificar Logs
Observar nos logs:
- ✅ "Agente criado com sucesso - ID: [guid]"
- ✅ Ausência de erros relacionados ao campo 'description'
- ✅ GenesysUserId armazenado no campo employeeid

## Resultado Esperado

- ✅ Usuários migram sem erro de campo inválido
- ✅ GenesysUserId preservado no campo employeeid
- ✅ Dados essenciais (nome, email, título, status) mantidos
- ⚠️ Department temporariamente não mapeado (requer análise de campos customizados)

## Próximos Passos

1. **Identificar campo para Department**: Verificar se existe campo customizado ou usar businessunitid
2. **Validar employeeid**: Confirmar se o campo aceita strings/GUIDs do Genesys
3. **Testes completos**: Executar migração com múltiplos usuários

## Status

- ✅ **Erro corrigido**: Campo 'description' removido
- ✅ **Build funcionando**: Sem erros de compilação
- ✅ **Mapeamento básico**: Campos essenciais preservados
- 🔄 **Em análise**: Mapeamento completo de Department