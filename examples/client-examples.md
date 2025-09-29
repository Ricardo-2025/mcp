# Exemplos de Integração com Azure Function MCP

Este documento contém exemplos de código para integração com a Azure Function MCP (Model Context Protocol) para migração entre Genesys Cloud e Microsoft Dynamics 365.

## Configuração Inicial

Antes de usar os exemplos, configure as seguintes variáveis de ambiente:

```bash
# URL da sua Azure Function
AZURE_FUNCTION_URL="https://[YOUR-FUNCTION-APP].azurewebsites.net"

# Chave de API da Azure Function
AZURE_FUNCTION_KEY="[YOUR-API-KEY]"

# ID da divisão no Genesys Cloud
GENESYS_DIVISION_ID="[YOUR-DIVISION-ID]"

# ID do ambiente no Dynamics 365
DYNAMICS_ENVIRONMENT_ID="[YOUR-ENVIRONMENT-ID]"
```

## Python

### Cliente Básico

```python
import requests
import json
import time
from typing import Dict, List, Optional

class GenesysMigrationMCPClient:
    def __init__(self, base_url: str, api_key: str):
        self.base_url = base_url.rstrip('/')
        self.headers = {
            'Authorization': f'Bearer {api_key}',
            'Content-Type': 'application/json'
        }
    
    def get_server_info(self) -> Dict:
        """Obtém informações do servidor MCP."""
        response = requests.get(f'{self.base_url}/mcp/info')
        response.raise_for_status()
        return response.json()
    
    def extract_flows(self, division_ids: List[str], flow_types: List[str] = None, include_inactive: bool = False) -> Dict:
        """Extrai flows do Genesys Cloud."""
        payload = {
            'divisionIds': division_ids,
            'flowTypes': flow_types or ['inboundcall'],
            'includeInactive': include_inactive
        }
        
        response = requests.post(
            f'{self.base_url}/mcp/extract-flows',
            headers=self.headers,
            json=payload
        )
        response.raise_for_status()
        return response.json()
    
    def migrate_to_dynamics(self, migration_id: str, flows: List[Dict]) -> Dict:
        """Migra flows para Microsoft Dynamics."""
        payload = {
            'migrationId': migration_id,
            'flows': flows
        }
        
        response = requests.post(
            f'{self.base_url}/mcp/migrate-to-dynamics',
            headers=self.headers,
            json=payload
        )
        response.raise_for_status()
        return response.json()
    
    def get_migration_status(self, migration_id: str) -> Dict:
        """Verifica o status de uma migração."""
        response = requests.get(
            f'{self.base_url}/mcp/migration-status/{migration_id}',
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()['data']
    
    def complete_migration(self, extraction_params: Dict, migration_params: Dict) -> Dict:
        """Executa migração completa (extração + migração)."""
        payload = {
            'extractionParams': extraction_params,
            'migrationParams': migration_params
        }
        
        response = requests.post(
            f'{self.base_url}/mcp/complete-migration',
            headers=self.headers,
            json=payload
        )
        response.raise_for_status()
        return response.json()
    
    def wait_for_completion(self, migration_id: str, max_wait_seconds: int = 300, poll_interval: int = 5) -> Dict:
        """Aguarda a conclusão de uma migração."""
        start_time = time.time()
        
        while time.time() - start_time < max_wait_seconds:
            status = self.get_migration_status(migration_id)
            
            print(f"Status: {status['status']}, Progresso: {status['progress']}%")
            
            if status['status'] == 'completed':
                return status
            elif status['status'] == 'failed':
                raise Exception(f"Migração falhou: {', '.join(status.get('errors', []))}")
            
            time.sleep(poll_interval)
        
        raise TimeoutError(f"Timeout aguardando conclusão da migração {migration_id}")

# Exemplo de uso
if __name__ == '__main__':
    client = GenesysMigrationMCPClient(
        base_url='https://sua-function-app.azurewebsites.net',
        api_key='sua-api-key'
    )
    
    try:
        # Verificar informações do servidor
        info = client.get_server_info()
        print(f"Servidor: {info['data']['name']} v{info['data']['version']}")
        
        # Executar migração completa
        result = client.complete_migration(
            extraction_params={
                'divisionIds': ['division-123'],
                'flowTypes': ['inboundcall', 'inboundchat'],
                'includeInactive': False
            },
            migration_params={
                'defaultLanguage': 'pt-BR',
                'createBots': True
            }
        )
        
        migration_id = result['data']['migrationId']
        print(f"Migração iniciada: {migration_id}")
        
        # Aguardar conclusão
        final_status = client.wait_for_completion(migration_id)
        print(f"Migração concluída! Itens processados: {final_status['itemsProcessed']}")
        
    except Exception as e:
        print(f"Erro: {e}")
```

## JavaScript/Node.js

### Cliente com Axios

```javascript
const axios = require('axios');

class GenesysMigrationMCPClient {
    constructor(baseUrl, apiKey) {
        this.baseUrl = baseUrl.replace(/\/$/, '');
        this.client = axios.create({
            baseURL: this.baseUrl,
            headers: {
                'Authorization': `Bearer ${apiKey}`,
                'Content-Type': 'application/json'
            },
            timeout: 30000
        });
        
        // Interceptor para tratamento de erros
        this.client.interceptors.response.use(
            response => response,
            error => {
                console.error('Erro na requisição:', error.response?.data || error.message);
                throw error;
            }
        );
    }
    
    async getServerInfo() {
        const response = await axios.get(`${this.baseUrl}/mcp/info`);
        return response.data;
    }
    
    async extractFlows(divisionIds, flowTypes = ['inboundcall'], includeInactive = false) {
        const response = await this.client.post('/mcp/extract-flows', {
            divisionIds,
            flowTypes,
            includeInactive
        });
        return response.data;
    }
    
    async migrateToDynamics(migrationId, flows) {
        const response = await this.client.post('/mcp/migrate-to-dynamics', {
            migrationId,
            flows
        });
        return response.data;
    }
    
    async getMigrationStatus(migrationId) {
        const response = await this.client.get(`/mcp/migration-status/${migrationId}`);
        return response.data.data;
    }
    
    async completeMigration(extractionParams, migrationParams) {
        const response = await this.client.post('/mcp/complete-migration', {
            extractionParams,
            migrationParams
        });
        return response.data;
    }
    
    async waitForCompletion(migrationId, maxWaitMs = 300000, pollIntervalMs = 5000) {
        const startTime = Date.now();
        
        while (Date.now() - startTime < maxWaitMs) {
            const status = await this.getMigrationStatus(migrationId);
            
            console.log(`Status: ${status.status}, Progresso: ${status.progress}%`);
            
            if (status.status === 'completed') {
                return status;
            } else if (status.status === 'failed') {
                throw new Error(`Migração falhou: ${status.errors?.join(', ')}`);
            }
            
            await new Promise(resolve => setTimeout(resolve, pollIntervalMs));
        }
        
        throw new Error(`Timeout aguardando conclusão da migração ${migrationId}`);
    }
}

// Exemplo de uso
async function main() {
    const client = new GenesysMigrationMCPClient(
        'https://sua-function-app.azurewebsites.net',
        'sua-api-key'
    );
    
    try {
        // Verificar servidor
        const info = await client.getServerInfo();
        console.log(`Servidor: ${info.data.name} v${info.data.version}`);
        
        // Migração por etapas
        console.log('Extraindo flows...');
        const extraction = await client.extractFlows(['division-123']);
        const migrationId = extraction.data.migrationId;
        const flows = extraction.data.flows;
        
        console.log(`Extraídos ${flows.length} flows`);
        
        // Preparar dados para migração
        const migrationFlows = flows.map(flow => ({
            genesysFlowId: flow.id,
            workstreamName: `${flow.name} Workstream`,
            botConfiguration: {
                name: `${flow.name} Bot`,
                language: 'pt-BR'
            }
        }));
        
        console.log('Iniciando migração...');
        await client.migrateToDynamics(migrationId, migrationFlows);
        
        // Aguardar conclusão
        const finalStatus = await client.waitForCompletion(migrationId);
        console.log('Migração concluída!', finalStatus);
        
    } catch (error) {
        console.error('Erro:', error.message);
    }
}

if (require.main === module) {
    main();
}

module.exports = GenesysMigrationMCPClient;
```

## C#

### Cliente .NET

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class GenesysMigrationMCPClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    
    public GenesysMigrationMCPClient(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
    
    public async Task<ServerInfo> GetServerInfoAsync()
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/mcp/info");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<ServerInfo>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return result.Data;
    }
    
    public async Task<ExtractionResult> ExtractFlowsAsync(string[] divisionIds, string[] flowTypes = null, bool includeInactive = false)
    {
        var payload = new
        {
            DivisionIds = divisionIds,
            FlowTypes = flowTypes ?? new[] { "inboundcall" },
            IncludeInactive = includeInactive
        };
        
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync($"{_baseUrl}/mcp/extract-flows", content);
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<ExtractionResult>>(responseJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return result.Data;
    }
    
    public async Task<MigrationResult> MigrateToDynamicsAsync(string migrationId, MigrationFlow[] flows)
    {
        var payload = new
        {
            MigrationId = migrationId,
            Flows = flows
        };
        
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync($"{_baseUrl}/mcp/migrate-to-dynamics", content);
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<MigrationResult>>(responseJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return result.Data;
    }
    
    public async Task<MigrationStatus> GetMigrationStatusAsync(string migrationId)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/mcp/migration-status/{migrationId}");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<MigrationStatus>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return result.Data;
    }
    
    public async Task<MigrationStatus> WaitForCompletionAsync(string migrationId, TimeSpan maxWaitTime = default, TimeSpan pollInterval = default, CancellationToken cancellationToken = default)
    {
        if (maxWaitTime == default) maxWaitTime = TimeSpan.FromMinutes(5);
        if (pollInterval == default) pollInterval = TimeSpan.FromSeconds(5);
        
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var status = await GetMigrationStatusAsync(migrationId);
            
            Console.WriteLine($"Status: {status.Status}, Progresso: {status.Progress}%");
            
            if (status.Status == "completed")
            {
                return status;
            }
            else if (status.Status == "failed")
            {
                throw new InvalidOperationException($"Migração falhou: {string.Join(", ", status.Errors ?? new string[0])}");
            }
            
            await Task.Delay(pollInterval, cancellationToken);
        }
        
        throw new TimeoutException($"Timeout aguardando conclusão da migração {migrationId}");
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// Modelos de dados
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T Data { get; set; }
    public string Error { get; set; }
}

public class ServerInfo
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string[] Capabilities { get; set; }
}

public class ExtractionResult
{
    public string MigrationId { get; set; }
    public GenesysFlow[] Flows { get; set; }
    public int TotalCount { get; set; }
}

public class GenesysFlow
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public string Division { get; set; }
}

public class MigrationFlow
{
    public string GenesysFlowId { get; set; }
    public string WorkstreamName { get; set; }
    public BotConfiguration BotConfiguration { get; set; }
}

public class BotConfiguration
{
    public string Name { get; set; }
    public string Language { get; set; }
}

public class MigrationResult
{
    public string MigrationId { get; set; }
    public string Status { get; set; }
    public string Message { get; set; }
}

public class MigrationStatus
{
    public string MigrationId { get; set; }
    public string Status { get; set; }
    public int Progress { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsTotal { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string[] Errors { get; set; }
}

// Exemplo de uso
class Program
{
    static async Task Main(string[] args)
    {
        using var client = new GenesysMigrationMCPClient(
            "https://sua-function-app.azurewebsites.net",
            "sua-api-key"
        );
        
        try
        {
            // Verificar servidor
            var info = await client.GetServerInfoAsync();
            Console.WriteLine($"Servidor: {info.Name} v{info.Version}");
            
            // Extrair flows
            var extraction = await client.ExtractFlowsAsync(new[] { "division-123" });
            Console.WriteLine($"Extraídos {extraction.Flows.Length} flows");
            
            // Preparar migração
            var migrationFlows = new List<MigrationFlow>();
            foreach (var flow in extraction.Flows)
            {
                migrationFlows.Add(new MigrationFlow
                {
                    GenesysFlowId = flow.Id,
                    WorkstreamName = $"{flow.Name} Workstream",
                    BotConfiguration = new BotConfiguration
                    {
                        Name = $"{flow.Name} Bot",
                        Language = "pt-BR"
                    }
                });
            }
            
            // Executar migração
            await client.MigrateToDynamicsAsync(extraction.MigrationId, migrationFlows.ToArray());
            
            // Aguardar conclusão
            var finalStatus = await client.WaitForCompletionAsync(extraction.MigrationId);
            Console.WriteLine($"Migração concluída! Itens processados: {finalStatus.ItemsProcessed}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }
    }
}
```

## PowerShell

### Script PowerShell

```powershell
# GenesysMigrationMCP PowerShell Client

class GenesysMigrationMCPClient {
    [string]$BaseUrl
    [hashtable]$Headers
    
    GenesysMigrationMCPClient([string]$baseUrl, [string]$apiKey) {
        $this.BaseUrl = $baseUrl.TrimEnd('/')
        $this.Headers = @{
            'Authorization' = "Bearer $apiKey"
            'Content-Type' = 'application/json'
        }
    }
    
    [object] GetServerInfo() {
        $uri = "$($this.BaseUrl)/mcp/info"
        $response = Invoke-RestMethod -Uri $uri -Method Get
        return $response
    }
    
    [object] ExtractFlows([string[]]$divisionIds, [string[]]$flowTypes = @('inboundcall'), [bool]$includeInactive = $false) {
        $uri = "$($this.BaseUrl)/mcp/extract-flows"
        $body = @{
            divisionIds = $divisionIds
            flowTypes = $flowTypes
            includeInactive = $includeInactive
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $this.Headers -Body $body
        return $response
    }
    
    [object] MigrateToDynamics([string]$migrationId, [array]$flows) {
        $uri = "$($this.BaseUrl)/mcp/migrate-to-dynamics"
        $body = @{
            migrationId = $migrationId
            flows = $flows
        } | ConvertTo-Json -Depth 10
        
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $this.Headers -Body $body
        return $response
    }
    
    [object] GetMigrationStatus([string]$migrationId) {
        $uri = "$($this.BaseUrl)/mcp/migration-status/$migrationId"
        $response = Invoke-RestMethod -Uri $uri -Method Get -Headers $this.Headers
        return $response.data
    }
    
    [object] WaitForCompletion([string]$migrationId, [int]$maxWaitSeconds = 300, [int]$pollIntervalSeconds = 5) {
        $startTime = Get-Date
        
        while (((Get-Date) - $startTime).TotalSeconds -lt $maxWaitSeconds) {
            $status = $this.GetMigrationStatus($migrationId)
            
            Write-Host "Status: $($status.status), Progresso: $($status.progress)%"
            
            if ($status.status -eq 'completed') {
                return $status
            }
            elseif ($status.status -eq 'failed') {
                throw "Migração falhou: $($status.errors -join ', ')"
            }
            
            Start-Sleep -Seconds $pollIntervalSeconds
        }
        
        throw "Timeout aguardando conclusão da migração $migrationId"
    }
}

# Exemplo de uso
try {
    $client = [GenesysMigrationMCPClient]::new(
        'https://sua-function-app.azurewebsites.net',
        'sua-api-key'
    )
    
    # Verificar servidor
    $info = $client.GetServerInfo()
    Write-Host "Servidor: $($info.data.name) v$($info.data.version)"
    
    # Extrair flows
    $extraction = $client.ExtractFlows(@('division-123'))
    Write-Host "Extraídos $($extraction.data.flows.Count) flows"
    
    # Preparar migração
    $migrationFlows = @()
    foreach ($flow in $extraction.data.flows) {
        $migrationFlows += @{
            genesysFlowId = $flow.id
            workstreamName = "$($flow.name) Workstream"
            botConfiguration = @{
                name = "$($flow.name) Bot"
                language = 'pt-BR'
            }
        }
    }
    
    # Executar migração
    $client.MigrateToDynamics($extraction.data.migrationId, $migrationFlows)
    
    # Aguardar conclusão
    $finalStatus = $client.WaitForCompletion($extraction.data.migrationId)
    Write-Host "Migração concluída! Itens processados: $($finalStatus.itemsProcessed)"
}
catch {
    Write-Error "Erro: $($_.Exception.Message)"
}
```

## Tratamento de Erros

### Códigos de Status HTTP Comuns

- **200 OK**: Requisição bem-sucedida
- **400 Bad Request**: Parâmetros inválidos
- **401 Unauthorized**: API key inválida ou ausente
- **429 Too Many Requests**: Rate limit excedido
- **500 Internal Server Error**: Erro interno do servidor

### Exemplo de Tratamento de Erros (Python)

```python
import requests
from requests.exceptions import RequestException
import time

def handle_api_error(response):
    """Trata erros da API de forma padronizada."""
    if response.status_code == 401:
        raise Exception("API key inválida. Verifique suas credenciais.")
    elif response.status_code == 429:
        retry_after = int(response.headers.get('Retry-After', 60))
        raise Exception(f"Rate limit excedido. Tente novamente em {retry_after} segundos.")
    elif response.status_code >= 500:
        raise Exception(f"Erro interno do servidor: {response.status_code}")
    else:
        try:
            error_data = response.json()
            raise Exception(f"Erro da API: {error_data.get('error', 'Erro desconhecido')}")
        except:
            raise Exception(f"Erro HTTP {response.status_code}: {response.text}")

def make_request_with_retry(func, max_retries=3, backoff_factor=2):
    """Executa uma requisição com retry automático."""
    for attempt in range(max_retries):
        try:
            return func()
        except requests.exceptions.Timeout:
            if attempt == max_retries - 1:
                raise Exception("Timeout na requisição após múltiplas tentativas")
            time.sleep(backoff_factor ** attempt)
        except requests.exceptions.ConnectionError:
            if attempt == max_retries - 1:
                raise Exception("Erro de conexão após múltiplas tentativas")
            time.sleep(backoff_factor ** attempt)
        except Exception as e:
            if "rate limit" in str(e).lower() and attempt < max_retries - 1:
                time.sleep(60)  # Aguardar 1 minuto em caso de rate limit
            else:
                raise
```

## Configuração de Logging

### Python com logging

```python
import logging

# Configurar logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler('genesys_migration.log'),
        logging.StreamHandler()
    ]
)

logger = logging.getLogger('GenesysMigrationMCP')

# Usar no cliente
class GenesysMigrationMCPClient:
    def __init__(self, base_url, api_key):
        self.logger = logging.getLogger(self.__class__.__name__)
        # ... resto da implementação
    
    def extract_flows(self, division_ids, flow_types=None, include_inactive=False):
        self.logger.info(f"Extraindo flows para divisões: {division_ids}")
        try:
            result = # ... fazer requisição
            self.logger.info(f"Extração concluída: {len(result['data']['flows'])} flows")
            return result
        except Exception as e:
            self.logger.error(f"Erro na extração: {e}")
            raise
```

Esses exemplos fornecem uma base sólida para integração com a Azure Function MCP em diferentes linguagens e cenários de uso.