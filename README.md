# Padroes de Resiliencia - Demo Interativa

Demonstracao pratica dos padroes de resiliencia em microsservicos usando .NET 8, Polly e uma interface web interativa. Projeto complementar da Aula 03 de Microservice and Web Engineering (FIAP).

## Padroes Demonstrados

- **Circuit Breaker**: Monitora falhas e bloqueia chamadas a servicos instaveis. Estados: Closed, Open e Half-Open.
- **Retry com Exponential Backoff**: Reexecuta operacoes com intervalos crescentes (1s, 2s, 4s).
- **Bulkhead**: Limita a 5 chamadas concorrentes, protegendo o servico de sobrecarga.
- **Fallback**: Retorna resposta padrao quando o servico esta indisponivel.

## Pre-requisitos

- Docker
- Docker Compose

## Como Executar

```bash
docker compose up --build
```

Acesse a aplicacao em: **http://localhost:8080**

## Estrutura do Projeto

```
padroes-de-resiliencia/
├── docker-compose.yml        # Orquestra os containers api e frontend
├── Dockerfile.api            # Build multi-stage da API .NET 8
├── Dockerfile.frontend       # Nginx servindo o frontend + proxy reverso
├── api/
│   ├── padroes-de-resiliencia-api.csproj
│   └── Program.cs            # API com Polly (Circuit Breaker, Retry, Bulkhead, Fallback)
└── frontend/
    ├── index.html            # Interface web interativa
    └── nginx.conf            # Configuracao do proxy reverso para /api
```

## Endpoints da API

| Metodo | Rota                | Descricao                                       |
|--------|---------------------|-------------------------------------------------|
| GET    | /api/data           | Chama o servico (com Circuit Breaker + Retry + Bulkhead + Fallback) |
| POST   | /api/force-failure  | Ativa modo de falha forcada                     |
| POST   | /api/restore        | Desativa modo de falha                          |
| GET    | /api/status         | Retorna estado do circuit breaker e bulkhead    |

## Como Usar a Demo

1. Clique em **Enviar Requisicao** -- observe que o circuit breaker esta Closed e a requisicao tem sucesso.
2. Clique em **Forcar Falha** -- ativa o modo de falha no servico downstream.
3. Clique em **Enviar Requisicao** varias vezes -- apos 3 falhas o circuit breaker muda para Open.
4. Com o circuito aberto, novas requisicoes acionam o **Fallback** imediatamente (sem chamar o servico).
5. Aguarde 15 segundos -- o circuit breaker muda para **Half-Open** e permite uma chamada de teste.
6. Clique em **Restaurar Servico** e envie uma requisicao -- o circuito volta para Closed.
7. Clique em **Enviar Rajada (10x)** -- dispara 10 requisicoes simultaneas para demonstrar o **Bulkhead** limitando a concorrencia.

## Comandos Uteis

```bash
# Parar os containers
docker compose down

# Rebuild forcado
docker compose up --build --force-recreate

# Ver logs da API em tempo real
docker compose logs -f api
```

## Codigo de Referencia (Polly)

O codigo da API segue o mesmo padrao mostrado nos slides da aula:

```csharp
// Circuit Breaker
var circuitBreaker = Policy
    .Handle<Exception>()
    .CircuitBreakerAsync(3, TimeSpan.FromSeconds(15),
        onBreak: (ex, ts) => Console.WriteLine($"Circuit OPEN por {ts.TotalSeconds}s"),
        onReset: () => Console.WriteLine("Circuit CLOSED"),
        onHalfOpen: () => Console.WriteLine("Circuit HALF-OPEN"));

// Retry com Exponential Backoff
var retry = Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(new[] {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    });

// Composicao de politicas
var policyWrap = Policy.WrapAsync(retry, circuitBreaker);
await policyWrap.ExecuteAsync(() => ServicoInstavel());
```
