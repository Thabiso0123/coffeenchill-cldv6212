# CoffeeNChill Canteen Management System — Part 2

Cloud platform for the CoffeeNChill campus canteen.

- **Part 1** replaced paper menus and filing-cabinet documents with HTTP-triggered Azure Functions backed by
  **Azure Table Storage** (menu items) and **Azure Blob Storage** (staff documents).
- **Part 2** removes the peak-hour bottleneck at the till. Orders are no longer processed synchronously:
  they are validated, serialised to JSON and pushed onto an **Azure Storage Queue**
  (`order-processing-queue`). A **queue-triggered Azure Function** (`ProcessOrderQueue`) picks each order up
  in the background, logs it in the **Orders** Azure Table and moves it through the barista lifecycle
  (`Received → Preparing → Ready → Collected`). The whole stack (Azurite + Functions host) now starts with a
  single `docker compose up`, pulling version-tagged images directly from Docker Hub.

> **Quick start (Part 2):** set your Docker Hub username in `.env`, then run `docker compose up -d` from the
> repository root. The API is available at `http://localhost:7071/api`.

---

## Changelog

### v2.0 — Part 2: Queue Triggers & Docker Compose Orchestration

**Added**
- `POST /api/orders/queue` — queue producer (`QueueOrderFunction`). Validates the client payload
  (customer name, 1–20 SKUs, SKU format, optional `OrderId` format, optional `TotalPrice`), checks every SKU
  exists on the menu and is available, calculates the authoritative total from menu prices, generates the
  `OrderId` (when not supplied) and a UTC `OrderTimestamp`, serialises the order to JSON and sends it
  **Base64-encoded** to `order-processing-queue`. Returns `202 Accepted` with a `Location` header immediately.
- `ProcessOrderQueue` — `[QueueTrigger("order-processing-queue")]` function. Parses and validates the JSON
  payload, writes the order to the **Orders** table (`PartitionKey = OrderDate`, `RowKey = OrderId`,
  `Status = "Received"`) and simulates the barista lifecycle `Received → Preparing → Ready → Collected`,
  recording each transition (with timestamps) in `StatusHistory`.
- `ProcessOrderPoisonQueue` — `[QueueTrigger("order-processing-queue-poison")]` function. Messages that fail
  `maxDequeueCount` (3) times are logged at **Critical** level and stored in a **FailedOrders** table for
  staff review.
- `GET /api/orders`, `GET /api/orders?status=…&date=…`, `GET /api/orders/{orderId}` — read orders and their
  status history back from the Orders table.
- `PUT /api/orders/status` — staff status override (forward-only transitions, `409` on backwards moves).
- `GET /api/orders/queue/status` — peeks `order-processing-queue` and reports the poison queue depth.
- `docker-compose.yml` in the repository root orchestrating **Azurite** and the **Functions host** on a custom
  bridge network (`coffeenchill-net`) with a named volume for Azurite data, a health check, and images pulled
  from Docker Hub (`coffeenchill-functions:v2.0`, `coffeenchill-azurite:v1.0`).
- `.env` file holding the Docker Hub username and the order stage delay used by Compose.
- **Orders (Queue Processing)** folder in the Postman collection (23 requests with automated tests).
- Root `.gitignore`.

**Changed**
- `host.json` now configures the queue extension (`messageEncoding: base64`, `maxDequeueCount: 3`,
  `batchSize: 8`, `visibilityTimeout: 10s`, `maxPollingInterval: 2s`).
- `ITableStorageService` / `TableStorageService` gained `GetMenuItemsBySkusAsync` so orders can be validated
  against the live menu.
- `POST /api/menu` now returns `409 Conflict` (instead of `500`) when the category/SKU already exists.
- `local.settings.json.example` includes `OrderProcessing__StageDelaySeconds`.

### v1.0 — Part 1: Azure Functions, Tables, Files & Docker Hub
- Menu Items CRUD over Azure Table Storage, Staff Documents over Blob Storage, standalone Docker containers,
  Docker Hub image `coffeenchill-functions:v1.0`, Postman collection.

---

## Architecture Overview

```
                         HTTP (JSON / multipart)
   Postman / Client  ───────────────────────────────►  Azure Functions (isolated worker, .NET 10)
                                                          │
                                    ┌─────────────────────┴─────────────────────┐
                                    │                                           │
                          ITableStorageService                        IFileStorageService
                                    │                                           │
                                    ▼                                           ▼
                          Azure Table Storage                          Azure Blob Storage
                          Table: MenuItems                             Container: staff-docs
                                    │                                           │
                                    └─────────────────  Azurite  ───────────────┘
                                              (local storage emulator container)
```

- Each HTTP function is thin: it validates input, calls a service, and shapes the response.
- Storage access is isolated behind `ITableStorageService` and `IFileStorageService`, which are
  registered in the dependency-injection container in `Program.cs`.
- In Part 1 the Functions container and the Azurite container run **separately** (no Docker Compose)
  and communicate over a user-defined Docker network.

### Part 2 — Asynchronous order workflow

```
   Student / Till                              docker compose  (network: coffeenchill-net)
        │                     ┌──────────────────────────────────────────────────────────────────────┐
        │ POST /orders/queue  │  functions  (<dockerhub_username>/coffeenchill-functions:v2.0)       │
        ├────────────────────►│  QueueOrderFunction ── validate ─► MenuItems table (SKU + price)     │
        │◄── 202 Accepted ────│        │ JSON → Base64                                               │
        │   (OrderId, Location)│        ▼                                                             │
        │                     │  order-processing-queue ───► ProcessOrderQueue  [QueueTrigger]       │
        │                     │        │ 3 failed attempts          │                                │
        │                     │        ▼                            ▼                                │
        │                     │  order-processing-queue-poison   Orders table                        │
        │                     │        │                         PK = OrderDate, RK = OrderId        │
        │                     │        ▼                         Received → Preparing → Ready →      │
        │                     │  ProcessOrderPoisonQueue           Collected (StatusHistory)         │
        │                     │        └──► FailedOrders table                                       │
        │ GET /orders/{id}    │                                                                      │
        ├────────────────────►│  GetOrderByIdFunction ──────────► Orders table                       │
        │                     ├──────────────────────────────────────────────────────────────────────┤
        │                     │  azurite  (<dockerhub_username>/coffeenchill-azurite:v1.0)            │
        │                     │  Blob :10000   Queue :10001   Table :10002   volume: azurite-data     │
        │                     └──────────────────────────────────────────────────────────────────────┘
```

- The producer never waits for preparation: it returns `202 Accepted` as soon as the message is on the queue,
  so hundreds of break-time orders can be accepted without blocking.
- Queue messages are Base64 encoded by the producer (`QueueClientOptions.MessageEncoding = Base64`) and decoded
  by the Functions host (`host.json → extensions.queues.messageEncoding = base64`).
- Order writes are idempotent: if a message is retried, the processor resumes from the order's current status
  instead of creating a duplicate. Status updates use ETag optimistic concurrency so a staff override and the
  background processor can never overwrite each other.

---

## API Endpoints

Base URL (local and container): `http://localhost:7071/api`

### Menu Items (Azure Table Storage)

| Method | Route                              | Description                                   |
|--------|------------------------------------|-----------------------------------------------|
| POST   | `/menu`                            | Create a menu item                            |
| GET    | `/menu`                            | Get all menu items                            |
| GET    | `/menu/category/{category}`        | Get items filtered by category (PartitionKey) |
| GET    | `/menu/item/{category}/{sku}`      | Get a single item by category and SKU         |
| PUT    | `/menu/{category}/{sku}`           | Update a menu item's price/availability        |
| DELETE | `/menu/{category}/{sku}`           | Delete a menu item                            |

**Create / Update body example**

```json
{
  "category": "Hot Drinks",
  "sku": "COF-001",
  "name": "Espresso",
  "description": "A rich single shot of espresso.",
  "price": 25.0,
  "isAvailable": true
}
```

> Categories that contain spaces (e.g. `Hot Drinks`) must be URL-encoded as `Hot%20Drinks`
> when calling the API by hand. Postman encodes this automatically.

### Staff Documents (Azure Blob Storage)

| Method | Route                                 | Description                                        |
|--------|---------------------------------------|----------------------------------------------------|
| POST   | `/documents/upload`                   | Upload a document (`multipart/form-data`, field `file`) |
| GET    | `/documents`                          | List documents (name, size, last modified)         |
| GET    | `/documents/download/{fileName}`      | Download a document                                |
| DELETE | `/documents/{fileName}`               | Delete a document                                  |

Allowed upload types: `.pdf`, `.doc`, `.docx` (max 10 MB).

### Orders (Azure Storage Queue + Orders Table) — Part 2

| Method | Route                                  | Description                                                        |
|--------|----------------------------------------|--------------------------------------------------------------------|
| POST   | `/orders/queue`                        | Validate an order and place it on `order-processing-queue` (`202`) |
| GET    | `/orders/queue/status?peek=10`         | Peek the order queue and report the poison queue depth             |
| GET    | `/orders`                              | List all orders (newest first)                                     |
| GET    | `/orders?status=Ready&date=2026-10-12` | Filter orders by status and/or order date                          |
| GET    | `/orders/{orderId}`                    | Get one order with its full status history                         |
| PUT    | `/orders/status`                       | Staff override: move an order forward in the lifecycle             |

| Trigger                                         | Function                  | Description                                           |
|-------------------------------------------------|---------------------------|-------------------------------------------------------|
| `QueueTrigger("order-processing-queue")`        | `ProcessOrderQueue`       | Logs the order in the Orders table and runs the lifecycle |
| `QueueTrigger("order-processing-queue-poison")` | `ProcessOrderPoisonQueue` | Logs failed messages and stores them in `FailedOrders` |

**Place order request** (`OrderId` and `TotalPrice` are optional — when omitted the API generates the id and
calculates the total from the menu; when supplied they are validated):

```json
{
  "CustomerName": "Jane Smith",
  "SelectedItemSKUs": ["COF-001", "PAS-104"],
  "TotalPrice": 65.00
}
```

**Message placed on `order-processing-queue`** (before Base64 encoding):

```json
{
  "OrderId": "ORD-2026-8801",
  "CustomerName": "Jane Smith",
  "SelectedItemSKUs": ["COF-001", "PAS-104"],
  "TotalPrice": 65.00,
  "OrderTimestamp": "2026-10-12T10:15:00Z"
}
```

**Orders table schema**

| Property          | Example                     | Notes                                            |
|-------------------|-----------------------------|--------------------------------------------------|
| PartitionKey      | `2026-10-12`                | Order date (`yyyy-MM-dd`, UTC)                    |
| RowKey            | `ORD-2026-8801`             | OrderId                                          |
| CustomerName      | `Jane Smith`                |                                                  |
| SelectedItemSKUs  | `COF-001,PAS-104`           | Comma separated (Table Storage has no arrays)    |
| TotalPrice        | `65.0`                      |                                                  |
| OrderTimestamp    | `2026-10-12T10:15:00Z`      | Generated by the producer                         |
| Status            | `Received` … `Collected`    | Current lifecycle stage                           |
| StatusHistory     | `[{"Status":"Received",…}]` | JSON list of every transition with its timestamp |
| QueueMessageId    |                             | Id of the queue message that created the order   |

**Status override request**

```json
{
  "OrderId": "ORD-2026-8801",
  "Status": "Ready"
}
```

**Error handling**

| Code  | When                                                                                       |
|-------|--------------------------------------------------------------------------------------------|
| `400` | Invalid JSON, missing/invalid fields, unknown or unavailable SKU, `TotalPrice` mismatch     |
| `404` | Order not found (it may still be waiting in the queue)                                     |
| `409` | Duplicate `OrderId`, or a backwards status transition                                      |
| `503` | The storage queue could not be reached                                                     |

**Lifecycle timing** — the processor waits `OrderProcessing__StageDelaySeconds` (default `5`) between each
stage so the transitions can be observed live in Postman or Azure Storage Explorer. Change it with
`ORDER_STAGE_DELAY_SECONDS` in `.env`.

---

## Running Locally

0. **On a fresh clone**, create your local settings from the template
   (`local.settings.json` is git-ignored and not committed):

   ```bash
   cp CoffeeNChill.Functions/local.settings.json.example CoffeeNChill.Functions/local.settings.json
   ```

1. **Start Azurite** (storage emulator) in its own container:

   ```bash
   docker run -d --name azurite \
     -p 10000:10000 -p 10001:10001 -p 10002:10002 \
     mcr.microsoft.com/azure-storage/azurite
   ```

   `local.settings.json` is already configured with `"AzureWebJobsStorage": "UseDevelopmentStorage=true"`,
   which points to Azurite on `localhost` using the default ports
   (Blob `10000`, Queue `10001`, Table `10002`).

2. **Run the Functions app**:

   ```bash
   cd CoffeeNChill.Functions
   func start
   ```

3. The API is now available at `http://localhost:7071/api`. The `MenuItems` table and the
   `staff-docs` container are created automatically on first use.

---

## Running with Docker (Standalone Containers)

Part 1 requires each component to run as its own container, wired together over a Docker network.
Replace `<dockerhub_username>` with your Docker Hub account name throughout.

1. **Create a network** so the two containers can find each other by name:

   ```bash
   docker network create coffeenchill-net
   ```

2. **Run Azurite** on that network:

   ```bash
   docker run -d --name azurite --network coffeenchill-net \
     -p 10000:10000 -p 10001:10001 -p 10002:10002 \
     mcr.microsoft.com/azure-storage/azurite
   ```

3. **Build the Functions image** (run from the `CoffeeNChill.Functions` folder, where the Dockerfile lives):

   ```bash
   cd CoffeeNChill.Functions
   docker build -t <dockerhub_username>/coffeenchill-functions:v1.0 .
   ```

4. **Run the Functions container**, passing the Azurite connection string as an environment variable.
   Because the app runs inside a container, `UseDevelopmentStorage=true` will **not** work — the host
   must be the Azurite **container name** (`azurite`), not `localhost`:

   ```bash
   docker run -d --name coffeenchill-functions --network coffeenchill-net \
     -p 7071:80 \
     -e AzureWebJobsStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;" \
     <dockerhub_username>/coffeenchill-functions:v1.0
   ```

   The API is now reachable at `http://localhost:7071/api`.

5. **View logs / stop / clean up** when you are done:

   ```bash
   docker logs -f coffeenchill-functions
   docker stop coffeenchill-functions azurite
   docker rm coffeenchill-functions azurite
   docker network rm coffeenchill-net
   ```

> The account name and key above are Azurite's fixed, publicly documented emulator credentials —
> they are safe to commit and are not real secrets.

---

## Running with Docker Compose (Part 2)

`docker-compose.yml` in the repository root starts the full stack with one command. It **pulls** the
published images from Docker Hub — no local build is needed.

| Service     | Image                                              | Ports                  |
|-------------|----------------------------------------------------|------------------------|
| `azurite`   | `<dockerhub_username>/coffeenchill-azurite:v1.0`   | 10000, 10001, 10002    |
| `functions` | `<dockerhub_username>/coffeenchill-functions:v2.0` | 7071 → 80              |

Both services join the custom bridge network `coffeenchill-net`, Azurite data is persisted in the named volume
`coffeenchill-azurite-data`, and the Functions host only starts once Azurite passes its health check.

1. Set your Docker Hub username in `.env` (repository root):

   ```env
   DOCKERHUB_USERNAME=your-dockerhub-username
   ORDER_STAGE_DELAY_SECONDS=5
   ```

2. Start everything:

   ```bash
   docker compose up -d
   ```

   Compose pulls `coffeenchill-functions:v2.0` and `coffeenchill-azurite:v1.0` from Docker Hub, creates the
   network and volume, waits for Azurite to be healthy and starts the Functions host.

3. Check the containers and follow the queue processing logs:

   ```bash
   docker compose ps
   docker compose logs -f functions
   ```

4. The API is available at `http://localhost:7071/api`.

5. Stop the stack (add `-v` to also delete the Azurite data volume):

   ```bash
   docker compose down
   ```

---

## Publishing Images to Docker Hub

Log in first:

```bash
docker login
```

**Functions image** (already built above):

```bash
docker push <dockerhub_username>/coffeenchill-functions:v1.0
```

**Functions image v2.0** (Part 2 — HTTP + Queue triggers). Build from the `CoffeeNChill.Functions` folder
and push so Docker Compose can pull it:

```bash
docker build -t <dockerhub_username>/coffeenchill-functions:v2.0 ./CoffeeNChill.Functions
docker push <dockerhub_username>/coffeenchill-functions:v2.0
```

**Azurite image** (re-tagged under your account, as required by the deliverables):

```bash
docker pull mcr.microsoft.com/azure-storage/azurite
docker tag mcr.microsoft.com/azure-storage/azurite <dockerhub_username>/coffeenchill-azurite:v1.0
docker push <dockerhub_username>/coffeenchill-azurite:v1.0
```

---

## Testing with Postman

1. Open Postman → **Import** → select both files from the `docs/` folder:
   - `CoffeeNChill.postman_collection.json`
   - `CoffeeNChill.postman_environment.json`
2. Select the **CoffeeNChill - Local** environment (top-right). It sets `baseUrl` to
   `http://localhost:7071/api`.
3. Run the requests in order, top to bottom. The **Menu Items** folder is fully automated:
   *Create → Get All → By Category → Get Single → Update → Delete*, plus negative tests
   that assert `400` and `404`.
4. For **Staff Documents**, open **Upload Staff Document**, attach a `.pdf`/`.doc`/`.docx`
   to the `file` field, and send. Then set the collection/environment variable `fileName`
   to the name you uploaded so **Download** and **Delete** target it.
5. Use **Run collection** to execute every request and view the passing test results.
6. **Orders (Queue Processing)** (Part 2) — run this folder with the **Collection Runner**, top to bottom:
   - *Seed Menu Item* requests make sure `COF-001` and `PAS-104` are on the menu.
   - *Place Order on Queue* sends the order, asserts `202`, the JSON payload attributes and the generated
     `OrderId` / `OrderTimestamp`, and stores `orderId` and `orderDate` as collection variables.
   - *Check Queue Status* shows the message on `order-processing-queue` (if it has not been consumed yet).
   - *Get Order – Logged as Received* waits `initialWaitSeconds` (3 s) and verifies the row in the Orders table.
   - *Get Order – Lifecycle Completed* waits `lifecycleWaitSeconds` (18 s) and verifies the history is
     exactly `Received → Preparing → Ready → Collected`.
   - Filtering, custom `OrderId`, staff override, and negative tests (`400`, `404`, `409`) follow.
   - *Cleanup* requests remove the seeded items so the collection can be re-run.

   If you change `ORDER_STAGE_DELAY_SECONDS`, set `lifecycleWaitSeconds` to at least `3 × delay + 3`.

---

## Team Contributions

| ST Number             | Name                                                                    | 
|-----------------------|-------------------------------------------------------------------------|
| ST10437073            | Hashmira Govender                                                       | 
| ST10483046            | Mbongeni Khumalo                                                        | 
| ST10470737            | Thabiso Ntlatleng                                                       | 

---

## Video Walkthrough

**Part 1** — running the Docker containers with `docker run` and executing the Postman collection:

[Part 1 video](https://youtu.be/lFEygfmwRxE)

**Part 2** — single-command `docker compose up`, image pulls from Docker Hub, orders landing on the queue,
live status updates in the Orders table and the Postman collection run:

[**[Part 2 unlisted YouTube link here]**](https://youtu.be/REPLACE_WITH_PART2_VIDEO_ID)

---

## AI Declaration

I have used AI to correct grammar and spelling errors in my syntax as per the student policy guild lines. All code was reviewed, built, run and tested by the team.

---
