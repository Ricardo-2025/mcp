using GenesysMigrationMCP.Models;
using GenesysMigrationMCP.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace GenesysMigrationMCP.Controllers
{
    /// <summary>
    /// Controlador para análise de mapeamento entre Genesys e Dynamics
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class MappingAnalysisController : ControllerBase
    {
        private readonly IMappingAnalysisService _mappingAnalysisService;
        private readonly ILogger<MappingAnalysisController> _logger;

        public MappingAnalysisController(
            IMappingAnalysisService mappingAnalysisService,
            ILogger<MappingAnalysisController> logger)
        {
            _mappingAnalysisService = mappingAnalysisService;
            _logger = logger;
        }

        /// <summary>
        /// Gera relatório completo de mapeamento entre Genesys e Dynamics
        /// </summary>
        /// <param name="request">Parâmetros para geração do relatório</param>
        /// <returns>Relatório completo de análise de mapeamento</returns>
        [HttpPost("generate-report")]
        [ProducesResponseType(typeof(MappingAnalysisReport), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<MappingAnalysisReport>> GenerateCompleteMappingReport(
            [FromBody] GenerateReportRequest request)
        {
            try
            {
                _logger.LogInformation("Iniciando geração de relatório de mapeamento");

                var report = await _mappingAnalysisService.GenerateCompleteMappingReportAsync(
                    request.IncludeDetailedAnalysis,
                    request.EntityTypes);

                _logger.LogInformation($"Relatório gerado com sucesso. ID: {report.ReportId}");

                return Ok(report);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos para geração de relatório");
                return BadRequest(new ProblemDetails
                {
                    Title = "Parâmetros Inválidos",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar relatório de mapeamento");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao gerar relatório",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Obtém resumo do mapeamento entre sistemas
        /// </summary>
        /// <returns>Resumo consolidado do mapeamento</returns>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(MappingSummary), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<MappingSummary>> GetMappingSummary()
        {
            try
            {
                _logger.LogInformation("Obtendo resumo de mapeamento");

                var summary = await _mappingAnalysisService.GetMappingSummaryAsync();

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter resumo de mapeamento");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao obter resumo",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Analisa mapeamento para um tipo específico de entidade
        /// </summary>
        /// <param name="entityType">Tipo de entidade (Users, Queues, Flows, etc.)</param>
        /// <returns>Análise detalhada do mapeamento para o tipo de entidade</returns>
        [HttpGet("entity-type/{entityType}")]
        [ProducesResponseType(typeof(EntityTypeMapping), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<EntityTypeMapping>> AnalyzeEntityTypeMapping(
            [FromRoute] string entityType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityType))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Parâmetro Obrigatório",
                        Detail = "Tipo de entidade é obrigatório",
                        Status = 400
                    });
                }

                _logger.LogInformation($"Analisando mapeamento para tipo de entidade: {entityType}");

                var mapping = await _mappingAnalysisService.AnalyzeEntityTypeMappingAsync(entityType);

                return Ok(mapping);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, $"Tipo de entidade inválido: {entityType}");
                return BadRequest(new ProblemDetails
                {
                    Title = "Tipo de Entidade Inválido",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao analisar mapeamento para {entityType}");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao analisar mapeamento",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Compara entidades específicas entre Genesys e Dynamics
        /// </summary>
        /// <param name="request">Dados para comparação de entidades</param>
        /// <returns>Comparação detalhada entre as entidades</returns>
        [HttpPost("compare-entities")]
        [ProducesResponseType(typeof(EntityComparison), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<EntityComparison>> CompareEntities(
            [FromBody] CompareEntitiesRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.GenesysEntityId) || 
                    string.IsNullOrWhiteSpace(request.EntityType))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Parâmetros Obrigatórios",
                        Detail = "ID da entidade Genesys e tipo de entidade são obrigatórios",
                        Status = 400
                    });
                }

                _logger.LogInformation($"Comparando entidades: Genesys {request.GenesysEntityId} com Dynamics {request.DynamicsEntityId}");

                var comparison = await _mappingAnalysisService.CompareEntitiesAsync(
                    request.GenesysEntityId,
                    request.DynamicsEntityId,
                    request.EntityType);

                return Ok(comparison);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos para comparação");
                return BadRequest(new ProblemDetails
                {
                    Title = "Parâmetros Inválidos",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao comparar entidades");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao comparar entidades",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Avalia riscos da migração
        /// </summary>
        /// <param name="entityTypes">Tipos de entidade para avaliação (opcional)</param>
        /// <returns>Avaliação completa de riscos</returns>
        [HttpPost("assess-risks")]
        [ProducesResponseType(typeof(RiskAssessment), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<RiskAssessment>> AssessMigrationRisks(
            [FromBody] List<string>? entityTypes = null)
        {
            try
            {
                _logger.LogInformation("Avaliando riscos da migração");

                var assessment = await _mappingAnalysisService.AssessMigrationRisksAsync(entityTypes);

                return Ok(assessment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao avaliar riscos da migração");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao avaliar riscos",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Analisa qualidade dos dados
        /// </summary>
        /// <param name="entityTypes">Tipos de entidade para análise (opcional)</param>
        /// <returns>Análise completa da qualidade dos dados</returns>
        [HttpPost("analyze-data-quality")]
        [ProducesResponseType(typeof(DataQualityAnalysis), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<DataQualityAnalysis>> AnalyzeDataQuality(
            [FromBody] List<string>? entityTypes = null)
        {
            try
            {
                _logger.LogInformation("Analisando qualidade dos dados");

                var analysis = await _mappingAnalysisService.AnalyzeDataQualityAsync(entityTypes);

                return Ok(analysis);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao analisar qualidade dos dados",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Gera plano de migração
        /// </summary>
        /// <param name="request">Parâmetros para geração do plano</param>
        /// <returns>Plano detalhado de migração</returns>
        [HttpPost("generate-migration-plan")]
        [ProducesResponseType(typeof(MigrationPlan), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<MigrationPlan>> GenerateMigrationPlan(
            [FromBody] GenerateMigrationPlanRequest request)
        {
            try
            {
                _logger.LogInformation($"Gerando plano de migração com estratégia: {request.Strategy}");

                var plan = await _mappingAnalysisService.GenerateMigrationPlanAsync(
                    request.Strategy,
                    request.PriorityEntityTypes);

                return Ok(plan);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos para plano de migração");
                return BadRequest(new ProblemDetails
                {
                    Title = "Parâmetros Inválidos",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar plano de migração");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao gerar plano de migração",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Obtém recomendações de migração
        /// </summary>
        /// <param name="category">Categoria das recomendações (opcional)</param>
        /// <param name="entityType">Tipo de entidade (opcional)</param>
        /// <returns>Lista de recomendações</returns>
        [HttpGet("recommendations")]
        [ProducesResponseType(typeof(List<MigrationRecommendation>), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<List<MigrationRecommendation>>> GetMigrationRecommendations(
            [FromQuery] string? category = null,
            [FromQuery] string? entityType = null)
        {
            try
            {
                _logger.LogInformation($"Obtendo recomendações - Categoria: {category}, Tipo: {entityType}");

                var recommendations = await _mappingAnalysisService.GetMigrationRecommendationsAsync(
                    category,
                    entityType);

                return Ok(recommendations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter recomendações");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao obter recomendações",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Valida migração de uma entidade específica
        /// </summary>
        /// <param name="request">Dados para validação</param>
        /// <returns>Resultado da validação</returns>
        [HttpPost("validate-entity-migration")]
        [ProducesResponseType(typeof(MigrationValidationResult), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<MigrationValidationResult>> ValidateEntityMigration(
            [FromBody] ValidateEntityMigrationRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.GenesysEntityId) || 
                    string.IsNullOrWhiteSpace(request.EntityType))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Parâmetros Obrigatórios",
                        Detail = "ID da entidade Genesys e tipo de entidade são obrigatórios",
                        Status = 400
                    });
                }

                _logger.LogInformation($"Validando migração da entidade: {request.GenesysEntityId} ({request.EntityType})");

                var result = await _mappingAnalysisService.ValidateEntityMigrationAsync(
                    request.GenesysEntityId,
                    request.EntityType);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parâmetros inválidos para validação");
                return BadRequest(new ProblemDetails
                {
                    Title = "Parâmetros Inválidos",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar migração da entidade");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao validar migração",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Analisa dependências entre entidades
        /// </summary>
        /// <param name="entityType">Tipo de entidade para análise</param>
        /// <returns>Mapa de dependências</returns>
        [HttpGet("dependencies/{entityType}")]
        [ProducesResponseType(typeof(Dictionary<string, List<string>>), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<ActionResult<Dictionary<string, List<string>>>> AnalyzeEntityDependencies(
            [FromRoute] string entityType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityType))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Parâmetro Obrigatório",
                        Detail = "Tipo de entidade é obrigatório",
                        Status = 400
                    });
                }

                _logger.LogInformation($"Analisando dependências para: {entityType}");

                var dependencies = await _mappingAnalysisService.AnalyzeEntityDependenciesAsync(entityType);

                return Ok(dependencies);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, $"Tipo de entidade inválido: {entityType}");
                return BadRequest(new ProblemDetails
                {
                    Title = "Tipo de Entidade Inválido",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao analisar dependências para {entityType}");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao analisar dependências",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Exporta relatório de mapeamento em diferentes formatos
        /// </summary>
        /// <param name="reportId">ID do relatório para exportação</param>
        /// <param name="format">Formato de exportação (JSON, Excel, PDF)</param>
        /// <returns>Arquivo do relatório no formato solicitado</returns>
        [HttpGet("export/{reportId}")]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 404)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<IActionResult> ExportMappingReport(
            [FromRoute] string reportId,
            [FromQuery] string format = "JSON")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reportId))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Parâmetro Obrigatório",
                        Detail = "ID do relatório é obrigatório",
                        Status = 400
                    });
                }

                _logger.LogInformation($"Exportando relatório {reportId} no formato {format}");

                // Para este exemplo, vamos gerar um novo relatório
                // Em uma implementação real, você buscaria o relatório pelo ID
                var report = await _mappingAnalysisService.GenerateCompleteMappingReportAsync();

                var fileContent = await _mappingAnalysisService.ExportMappingReportAsync(report, format);

                var contentType = format.ToUpper() switch
                {
                    "JSON" => "application/json",
                    "EXCEL" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "PDF" => "application/pdf",
                    _ => "application/octet-stream"
                };

                var fileName = $"mapping-report-{reportId}.{format.ToLower()}";

                return File(fileContent, contentType, fileName);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, $"Formato inválido: {format}");
                return BadRequest(new ProblemDetails
                {
                    Title = "Formato Inválido",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (NotImplementedException ex)
            {
                _logger.LogWarning(ex, $"Formato não implementado: {format}");
                return BadRequest(new ProblemDetails
                {
                    Title = "Formato Não Suportado",
                    Detail = ex.Message,
                    Status = 400
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao exportar relatório {reportId}");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao exportar relatório",
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Atualiza cache de dados
        /// </summary>
        /// <param name="forceRefresh">Forçar atualização mesmo se cache ainda válido</param>
        /// <returns>Status da atualização</returns>
        [HttpPost("refresh-cache")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<IActionResult> RefreshDataCache([FromQuery] bool forceRefresh = false)
        {
            try
            {
                _logger.LogInformation($"Atualizando cache de dados - Forçar: {forceRefresh}");

                var success = await _mappingAnalysisService.RefreshDataCacheAsync(forceRefresh);

                return Ok(new { success, message = success ? "Cache atualizado com sucesso" : "Falha ao atualizar cache" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar cache");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Erro Interno",
                    Detail = "Erro interno do servidor ao atualizar cache",
                    Status = 500
                });
            }
        }
    }

    #region Request Models

    /// <summary>
    /// Modelo para solicitação de geração de relatório
    /// </summary>
    public class GenerateReportRequest
    {
        /// <summary>
        /// Incluir análise detalhada no relatório
        /// </summary>
        public bool IncludeDetailedAnalysis { get; set; } = true;

        /// <summary>
        /// Tipos de entidade específicos para análise (opcional)
        /// </summary>
        public List<string>? EntityTypes { get; set; }
    }

    /// <summary>
    /// Modelo para solicitação de comparação de entidades
    /// </summary>
    public class CompareEntitiesRequest
    {
        /// <summary>
        /// ID da entidade no Genesys
        /// </summary>
        [Required]
        public string GenesysEntityId { get; set; } = string.Empty;

        /// <summary>
        /// ID da entidade no Dynamics (opcional)
        /// </summary>
        public string? DynamicsEntityId { get; set; }

        /// <summary>
        /// Tipo de entidade
        /// </summary>
        [Required]
        public string EntityType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Modelo para solicitação de geração de plano de migração
    /// </summary>
    public class GenerateMigrationPlanRequest
    {
        /// <summary>
        /// Estratégia de migração (Phased, BigBang, Parallel)
        /// </summary>
        public string Strategy { get; set; } = "Phased";

        /// <summary>
        /// Tipos de entidade prioritários (opcional)
        /// </summary>
        public List<string>? PriorityEntityTypes { get; set; }
    }

    /// <summary>
    /// Modelo para solicitação de validação de migração de entidade
    /// </summary>
    public class ValidateEntityMigrationRequest
    {
        /// <summary>
        /// ID da entidade no Genesys
        /// </summary>
        [Required]
        public string GenesysEntityId { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de entidade
        /// </summary>
        [Required]
        public string EntityType { get; set; } = string.Empty;
    }

    #endregion
}