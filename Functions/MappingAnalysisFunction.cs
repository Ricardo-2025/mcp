using GenesysMigrationMCP.Models;
using GenesysMigrationMCP.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace GenesysMigrationMCP.Functions
{
    /// <summary>
    /// Função Azure para análise de mapeamento entre Genesys e Dynamics
    /// </summary>
    public class MappingAnalysisFunction
    {
        private readonly IMappingAnalysisService _mappingAnalysisService;
        private readonly ILogger<MappingAnalysisFunction> _logger;

        public MappingAnalysisFunction(
            IMappingAnalysisService mappingAnalysisService,
            ILogger<MappingAnalysisFunction> logger)
        {
            _mappingAnalysisService = mappingAnalysisService;
            _logger = logger;
        }

        /// <summary>
        /// Gera relatório completo de mapeamento - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("GenerateCompleteMappingReport")]
        public async Task<HttpResponseData> GenerateCompleteMappingReport(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/generate-report")] HttpRequestData req)
        {
            _logger.LogInformation("Iniciando geração de relatório completo de mapeamento via Azure Function");

            try
            {
                // Ler parâmetros da requisição
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<GenerateReportRequest>(requestBody) ?? new GenerateReportRequest();

                // Gerar relatório
                var report = await _mappingAnalysisService.GenerateCompleteMappingReportAsync(
                    request.IncludeDetailedAnalysis,
                    request.EntityTypes);

                // Criar resposta
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(report, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                _logger.LogInformation($"Relatório gerado com sucesso. ID: {report.ReportId}");
                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Erro ao deserializar requisição");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Formato de requisição inválido", ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetros inválidos", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar relatório de mapeamento");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Obtém resumo do mapeamento - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("GetMappingSummary")]
        public async Task<HttpResponseData> GetMappingSummary(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "mapping/summary")] HttpRequestData req)
        {
            _logger.LogInformation("Obtendo resumo de mapeamento via Azure Function");

            try
            {
                var summary = await _mappingAnalysisService.GetMappingSummaryAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(summary, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter resumo de mapeamento");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Analisa mapeamento para tipo específico de entidade - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("AnalyzeEntityTypeMapping")]
        public async Task<HttpResponseData> AnalyzeEntityTypeMapping(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "mapping/entity-type/{entityType}")] HttpRequestData req,
            string entityType)
        {
            _logger.LogInformation($"Analisando mapeamento para tipo de entidade: {entityType}");

            try
            {
                if (string.IsNullOrWhiteSpace(entityType))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetro obrigatório", "Tipo de entidade é obrigatório");
                }

                var mapping = await _mappingAnalysisService.AnalyzeEntityTypeMappingAsync(entityType);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(mapping, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, $"Tipo de entidade inválido: {entityType}");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Tipo de entidade inválido", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao analisar mapeamento para {entityType}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Compara entidades entre sistemas - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("CompareEntities")]
        public async Task<HttpResponseData> CompareEntities(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/compare-entities")] HttpRequestData req)
        {
            _logger.LogInformation("Comparando entidades via Azure Function");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<CompareEntitiesRequest>(requestBody);

                if (request == null || string.IsNullOrWhiteSpace(request.GenesysEntityId) || string.IsNullOrWhiteSpace(request.EntityType))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetros obrigatórios", "ID da entidade Genesys e tipo de entidade são obrigatórios");
                }

                var comparison = await _mappingAnalysisService.CompareEntitiesAsync(
                    request.GenesysEntityId,
                    request.DynamicsEntityId,
                    request.EntityType);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(comparison, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Erro ao deserializar requisição");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Formato de requisição inválido", ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetros inválidos", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao comparar entidades");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Avalia riscos de migração - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("AssessMigrationRisks")]
        public async Task<HttpResponseData> AssessMigrationRisks(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/assess-risks")] HttpRequestData req)
        {
            _logger.LogInformation("Avaliando riscos da migração via Azure Function");

            try
            {
                List<string>? entityTypes = null;

                // Tentar ler tipos de entidade do corpo da requisição (opcional)
                try
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(requestBody))
                    {
                        entityTypes = JsonConvert.DeserializeObject<List<string>>(requestBody);
                    }
                }
                catch
                {
                    // Ignorar erro de deserialização - parâmetro é opcional
                }

                var assessment = await _mappingAnalysisService.AssessMigrationRisksAsync(entityTypes);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(assessment, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao avaliar riscos da migração");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Analisa qualidade dos dados - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("AnalyzeDataQuality")]
        public async Task<HttpResponseData> AnalyzeDataQuality(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/analyze-data-quality")] HttpRequestData req)
        {
            _logger.LogInformation("Analisando qualidade dos dados via Azure Function");

            try
            {
                List<string>? entityTypes = null;

                // Tentar ler tipos de entidade do corpo da requisição (opcional)
                try
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(requestBody))
                    {
                        entityTypes = JsonConvert.DeserializeObject<List<string>>(requestBody);
                    }
                }
                catch
                {
                    // Ignorar erro de deserialização - parâmetro é opcional
                }

                var analysis = await _mappingAnalysisService.AnalyzeDataQualityAsync(entityTypes);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(analysis, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Gera plano de migração - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("GenerateMigrationPlan")]
        public async Task<HttpResponseData> GenerateMigrationPlan(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/generate-migration-plan")] HttpRequestData req)
        {
            _logger.LogInformation("Gerando plano de migração via Azure Function");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<GenerateMigrationPlanRequest>(requestBody) ?? new GenerateMigrationPlanRequest();

                var plan = await _mappingAnalysisService.GenerateMigrationPlanAsync(
                    request.Strategy,
                    request.PriorityEntityTypes);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(plan, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Erro ao deserializar requisição");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Formato de requisição inválido", ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetros inválidos", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar plano de migração");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Obtém recomendações de migração - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("GetMigrationRecommendations")]
        public async Task<HttpResponseData> GetMigrationRecommendations(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "mapping/recommendations")] HttpRequestData req)
        {
            _logger.LogInformation("Obtendo recomendações de migração via Azure Function");

            try
            {
                // Extrair parâmetros de query
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var category = query["category"];
                var entityType = query["entityType"];

                var recommendations = await _mappingAnalysisService.GetMigrationRecommendationsAsync(category, entityType);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(recommendations, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter recomendações");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Valida migração de entidade - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("ValidateEntityMigration")]
        public async Task<HttpResponseData> ValidateEntityMigration(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/validate-entity-migration")] HttpRequestData req)
        {
            _logger.LogInformation("Validando migração de entidade via Azure Function");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<ValidateEntityMigrationRequest>(requestBody);

                if (request == null || string.IsNullOrWhiteSpace(request.GenesysEntityId) || string.IsNullOrWhiteSpace(request.EntityType))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetros obrigatórios", "ID da entidade Genesys e tipo de entidade são obrigatórios");
                }

                var result = await _mappingAnalysisService.ValidateEntityMigrationAsync(
                    request.GenesysEntityId,
                    request.EntityType);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(result, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Erro ao deserializar requisição");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Formato de requisição inválido", ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetros inválidos", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar migração da entidade");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Analisa dependências entre entidades - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("AnalyzeEntityDependencies")]
        public async Task<HttpResponseData> AnalyzeEntityDependencies(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "mapping/dependencies/{entityType}")] HttpRequestData req,
            string entityType)
        {
            _logger.LogInformation($"Analisando dependências para: {entityType}");

            try
            {
                if (string.IsNullOrWhiteSpace(entityType))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetro obrigatório", "Tipo de entidade é obrigatório");
                }

                var dependencies = await _mappingAnalysisService.AnalyzeEntityDependenciesAsync(entityType);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var jsonResponse = JsonConvert.SerializeObject(dependencies, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, $"Tipo de entidade inválido: {entityType}");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Tipo de entidade inválido", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao analisar dependências para {entityType}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Exporta relatório em diferentes formatos - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("ExportMappingReport")]
        public async Task<HttpResponseData> ExportMappingReport(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "mapping/export/{reportId}")] HttpRequestData req,
            string reportId)
        {
            _logger.LogInformation($"Exportando relatório {reportId}");

            try
            {
                if (string.IsNullOrWhiteSpace(reportId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Parâmetro obrigatório", "ID do relatório é obrigatório");
                }

                // Extrair formato da query string
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var format = query["format"] ?? "JSON";

                // Para este exemplo, gerar um novo relatório
                var report = await _mappingAnalysisService.GenerateCompleteMappingReportAsync();
                var fileContent = await _mappingAnalysisService.ExportMappingReportAsync(report, format);

                var response = req.CreateResponse(HttpStatusCode.OK);

                var contentType = format.ToUpper() switch
                {
                    "JSON" => "application/json",
                    "EXCEL" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "PDF" => "application/pdf",
                    _ => "application/octet-stream"
                };

                response.Headers.Add("Content-Type", contentType);
                response.Headers.Add("Content-Disposition", $"attachment; filename=mapping-report-{reportId}.{format.ToLower()}");

                await response.WriteBytesAsync(fileContent);

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Formato inválido");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Formato inválido", ex.Message);
            }
            catch (NotImplementedException ex)
            {
                _logger.LogWarning(ex, "Formato não implementado");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Formato não suportado", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao exportar relatório {reportId}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Atualiza cache de dados - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("RefreshDataCache")]
        public async Task<HttpResponseData> RefreshDataCache(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mapping/refresh-cache")] HttpRequestData req)
        {
            _logger.LogInformation("Atualizando cache de dados via Azure Function");

            try
            {
                // Extrair parâmetro de query
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var forceRefresh = bool.TryParse(query["forceRefresh"], out var force) && force;

                var success = await _mappingAnalysisService.RefreshDataCacheAsync(forceRefresh);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                var result = new { success, message = success ? "Cache atualizado com sucesso" : "Falha ao atualizar cache" };
                var jsonResponse = JsonConvert.SerializeObject(result, Formatting.Indented);
                await response.WriteStringAsync(jsonResponse);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar cache");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Erro interno", "Erro interno do servidor");
            }
        }

        /// <summary>
        /// Função de health check - DESABILITADO (apenas MCP deve estar ativo)
        /// </summary>
        // [Function("HealthCheck")]
        public async Task<HttpResponseData> HealthCheck(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mapping/health")] HttpRequestData req)
        {
            _logger.LogInformation("Health check executado");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            var healthStatus = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                service = "MappingAnalysisFunction"
            };

            var jsonResponse = JsonConvert.SerializeObject(healthStatus, Formatting.Indented);
            await response.WriteStringAsync(jsonResponse);

            return response;
        }

        #region Métodos Auxiliares

        /// <summary>
        /// Cria resposta de erro padronizada
        /// </summary>
        private async Task<HttpResponseData> CreateErrorResponse(
            HttpRequestData req, 
            HttpStatusCode statusCode, 
            string title, 
            string detail)
        {
            var response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            var errorResponse = new
            {
                title,
                detail,
                status = (int)statusCode,
                timestamp = DateTime.UtcNow
            };

            var jsonResponse = JsonConvert.SerializeObject(errorResponse, Formatting.Indented);
            await response.WriteStringAsync(jsonResponse);

            return response;
        }

        #endregion
    }

    #region Request Models para Azure Functions

    /// <summary>
    /// Modelo para solicitação de geração de relatório
    /// </summary>
    public class GenerateReportRequest
    {
        public bool IncludeDetailedAnalysis { get; set; } = true;
        public List<string>? EntityTypes { get; set; }
    }

    /// <summary>
    /// Modelo para solicitação de comparação de entidades
    /// </summary>
    public class CompareEntitiesRequest
    {
        public string GenesysEntityId { get; set; } = string.Empty;
        public string? DynamicsEntityId { get; set; }
        public string EntityType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Modelo para solicitação de geração de plano de migração
    /// </summary>
    public class GenerateMigrationPlanRequest
    {
        public string Strategy { get; set; } = "Phased";
        public List<string>? PriorityEntityTypes { get; set; }
    }

    /// <summary>
    /// Modelo para solicitação de validação de migração de entidade
    /// </summary>
    public class ValidateEntityMigrationRequest
    {
        public string GenesysEntityId { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
    }

    #endregion
}