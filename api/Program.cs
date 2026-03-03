using Polly;
using Polly.CircuitBreaker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// --- Estado global simulando serviço downstream ---
var forceFail = false;
var bulkheadSemaphore = new SemaphoreSlim(5, 5); // max 5 concorrentes

// --- Simulação de serviço instável (mesmo padrão do slide) ---
async Task<string> ServicoInstavel()
{
    await Task.Delay(200); // simula latência
    if (forceFail)
        throw new Exception("Falha simulada");
    return "Dados retornados com sucesso";
}

// --- Circuit Breaker com Polly (código semelhante ao slide) ---
var circuitBreaker = Policy
    .Handle<Exception>()
    .CircuitBreakerAsync(
        exceptionsAllowedBeforeBreaking: 3,
        durationOfBreak: TimeSpan.FromSeconds(15),
        onBreak: (ex, ts) => Console.WriteLine($"Circuit OPEN por {ts.TotalSeconds}s: {ex.Message}"),
        onReset: () => Console.WriteLine("Circuit CLOSED"),
        onHalfOpen: () => Console.WriteLine("Circuit HALF-OPEN"));

// --- Retry com Exponential Backoff (código semelhante ao slide) ---
var retry = Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    },
    onRetry: (ex, ts, attempt, _) =>
        Console.WriteLine($"Retry #{attempt} em {ts.TotalSeconds}s: {ex.Message}"));

// --- Policy wrap: retry envolve circuit breaker ---
var policyWrap = Policy.WrapAsync(retry, circuitBreaker);

// --- Fallback ---
var fallbackPolicy = Policy<string>
    .Handle<Exception>()
    .FallbackAsync(
        fallbackValue: "Resposta fallback: servico indisponivel no momento",
        onFallbackAsync: (ex, _) =>
        {
            Console.WriteLine($"Fallback acionado: {ex.Exception?.Message}");
            return Task.CompletedTask;
        });

// --- Endpoints ---

app.MapGet("/api/data", async () =>
{
    // Bulkhead: limita concorrência
    if (!await bulkheadSemaphore.WaitAsync(TimeSpan.FromMilliseconds(500)))
    {
        return Results.Json(new
        {
            success = false,
            message = "Bulkhead: limite de concorrencia atingido",
            pattern = "bulkhead",
            circuitState = circuitBreaker.CircuitState.ToString()
        }, statusCode: 429);
    }

    try
    {
        var result = await fallbackPolicy.ExecuteAsync(async () =>
        {
            return await policyWrap.ExecuteAsync(async () =>
            {
                return await ServicoInstavel();
            });
        });

        var isFallback = result.Contains("fallback");

        return Results.Json(new
        {
            success = !isFallback,
            message = result,
            pattern = isFallback ? "fallback" : "success",
            circuitState = circuitBreaker.CircuitState.ToString()
        });
    }
    finally
    {
        bulkheadSemaphore.Release();
    }
});

app.MapPost("/api/force-failure", () =>
{
    forceFail = true;
    Console.WriteLine("Modo de falha ATIVADO");
    return Results.Json(new { forceFail = true, message = "Falhas forcadas ativadas" });
});

app.MapPost("/api/restore", () =>
{
    forceFail = false;
    Console.WriteLine("Modo de falha DESATIVADO");
    return Results.Json(new { forceFail = false, message = "Servico restaurado" });
});

app.MapGet("/api/status", () =>
{
    return Results.Json(new
    {
        circuitState = circuitBreaker.CircuitState.ToString(),
        forceFail,
        bulkheadAvailable = bulkheadSemaphore.CurrentCount,
        bulkheadMax = 5
    });
});

app.Run();
