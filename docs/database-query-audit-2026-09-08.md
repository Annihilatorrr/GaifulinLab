# Аудит запросов к БД — 2026-09-08

Проверено текущее рабочее дерево, включая существовавшие до аудита изменения. Production-код не изменялся. Этот файл содержит наблюдения аудита, а не новые продуктовые правила.

## Область и достоверность

Проверены обращения к БД в `src`, вызывающие контроллеры, доменные методы, EF-конфигурации, миграции и релевантные тесты. Хранилище приложения — PostgreSQL через EF Core/Npgsql; Identity использует тот же `AppDbContext`. Основные запросы находятся в Application и Infrastructure; просмотры, PDF и health также обращаются к контексту из API. Это наблюдаемая структура: `docs/architecture.md`, `docs/product/*` и `docs/codex/business-rules-audit.md` отсутствуют.

Найдено **52 явных места выполнения чтения**, включая SQL-захват PDF-задачи и одну ветку только для InMemory; **12 прикладных вызовов SaveChangesAsync**, **2 прямые операции записи** (INSERT просмотров и ExecuteUpdate backfill). Дополнительно учтены обращения через Identity, проверка подключения и миграции. Это число точек исходного кода, а не SQL-команд на HTTP-запрос: SaveChanges может выполнять несколько INSERT/UPDATE/DELETE, а общий загрузчик вызывается из разных сценариев. Вложенные `Any`, `Join`, `Select` до материализации — части одного запроса, не отдельные походы в БД.

Ниже ошибки, подтверждённые анализом кода и ограничений. Конкурентные сценарии **не воспроизводились на PostgreSQL**; их последовательности приведены явно. Арифметическое переполнение отдельно проверено исполнением C#. Измерений latency, реальной статистики таблиц и фактических планов выполнения в этом аудите нет. SQL в рекомендациях обозначает форму решения, а не снятый трассировкой SQL EF.

## Ошибки

### B1 — Major: конкурентная замена таксономии сохраняет смешанный набор

Источник: [UpdateArticleTaxonomyCommandHandler.cs](../src/GaifulinLab.Application/Articles/UpdateArticleTaxonomy/UpdateArticleTaxonomyCommandHandler.cs), чтение 18–25, замена 51–52, сохранение 66; [Article.cs](../src/GaifulinLab.Domain/Articles/Article.cs), `ReplaceTopics`, `ReplaceTags`; [ArticleConfiguration.cs](../src/GaifulinLab.Infrastructure/Persistence/Configurations/ArticleConfiguration.cs).

Два запроса читают статью без тем. Первый заменяет темы на `[T1]`, второй на `[T2]`, где обе темы уже существуют. Каждый контекст видит пустой исходный набор и вставляет только свою связь. Обе записи допустимы по составному PK, а версия Article не проверяется. Итог `[T1,T2]` не соответствует ни одному запросу полной замены. Аналогично для тегов и добавления разных серий. Транзакционность каждого SaveChanges не сериализует предшествующие чтения.

Коррекция: общий механизм конкурентного изменения Article для замены связей, например версия агрегата, которая проверяется и меняется всеми затронутыми командами, либо сериализация операции по статье. Конфликт должен приводить к откату всей замены и понятному 409. Версия ArticleLocalization этот путь не защищает. Тест: два контекста с одинаковым исходным снимком, два разных полных набора; смешанный результат недопустим.

### B2 — Major: после soft delete можно вновь занять позицию серии

Источник: [DeleteArticleCommandHandler.cs](../src/GaifulinLab.Application/Articles/DeleteArticle/DeleteArticleCommandHandler.cs), 15–34; [UpdateArticleTaxonomyCommandHandler.cs](../src/GaifulinLab.Application/Articles/UpdateArticleTaxonomy/UpdateArticleTaxonomyCommandHandler.cs), 18–66; [ArticleSeriesConfiguration.cs](../src/GaifulinLab.Infrastructure/Persistence/Configurations/ArticleSeriesConfiguration.cs).

Запрос изменения читает живую статью без серий. Другой запрос удаляет её и завершает SaveChanges. Первый запрос затем добавляет связь с серией и сохраняется по устаревшему Article. Проверка `DeletedAt == null` была только в SELECT; поле не является concurrency token. Физическая строка Article остаётся, поэтому FK разрешает новую связь. Удалённая статья занимает уникальную `(SeriesId, Position)`, хотя публичные выборки её скрывают. Однократная миграция `ReleaseDeletedArticleSeriesLinks` не предотвращает повторение.

Коррекция: синхронизировать delete и все изменения статьи через одну проверку версии/блокировку. Простого повторного SELECT перед SaveChanges недостаточно. Тест: приостановить изменение после чтения, завершить удаление, продолжить изменение; новая связь не должна сохраниться.

### B3 — Major: PDF-worker может завершить чужую попытку

Источник: [PdfExportWorker.cs](../src/GaifulinLab.Infrastructure/Pdf/PdfExportWorker.cs), захват 58–80, завершение 104–106, ошибка 124–131; [PdfExportJobConfiguration.cs](../src/GaifulinLab.Infrastructure/Persistence/Configurations/PdfExportJobConfiguration.cs); [PdfExportJob.cs](../src/GaifulinLab.Domain/Pdf/PdfExportJob.cs), `Start`, `Complete`, `Fail`.

Worker A захватил попытку 1, затем задержался достаточно для истечения lease. Worker B захватил попытку 2. A сохраняет Completed по одному Id, без проверки AttemptCount; он может перезаписать уже готовый результат B. Если A завершился ошибкой, `FailAsync` проверяет только Processing и может пометить активную попытку B как Failed. Это требует перекрытия попыток, например из-за паузы процесса или задержки хранения/БД; обычный быстрый рендер сам по себе проблему не вызывает.

Коррекция: атомарный UPDATE по `Id + AttemptCount + Status=Processing` (или отдельному токену захвата), с проверкой числа обновлённых строк для Complete и Fail. При утрате захвата обработать оставшийся файл старой попытки. Проверки только в памяти недостаточно. Тест: повторный захват и поздние успех/ошибка первой попытки.

### B4 — Major: переполнение OFFSET публичного списка

Источник: [GetPublicArticlesQueryHandler.cs](../src/GaifulinLab.Application/Articles/Public/GetPublicArticles/GetPublicArticlesQueryHandler.cs), 28–29, 70; [PublicArticlesController.cs](../src/GaifulinLab.Api/Controllers/PublicArticlesController.cs), 41–51.

`Skip((page - 1) * pageSize)` вычисляется в int. Ограничение размера страницы не ограничивает произведение. Исполнением C# подтверждены:

- `page=21474838&pageSize=100` → offset `-2147483596`;
- `page=1073741825&pageSize=100` → offset `0`: заведомо далёкая страница превращается в первую.

Точный HTTP-ответ для отрицательного offset в этом аудите не проверялся. Нулевое смещение уже доказывает неправильную пагинацию независимо от обработки отрицательного значения провайдером.

Коррекция: вычислять смещение в long, проверять допустимый диапазон до Skip и явно возвращать пустую страницу либо ошибку валидации. Один `checked` без обработки только заменит неправильный результат исключением. Это относится к `/api/public/articles`; поиск отдельно ограничивает номер по totalPages.

### B5 — Major: потеря неудачных попыток входа при конфликте Identity

> Исправлено 2026-09-08 в `UserAuthenticationService`: `ConcurrencyFailure` при учёте или сбросе попыток повторяется с новым `UserManager` и актуальным состоянием пользователя. Повтор ограничен тремя попытками; прочие ошибки записываются в журнал.

Источник: [UserAuthenticationService.cs](../src/GaifulinLab.Infrastructure/Authentication/UserAuthenticationService.cs), 18–30. Возвращаемые IdentityResult от `AccessFailedAsync` и `ResetAccessFailedCountAsync` игнорируются.

Два контекста читают одну версию пользователя и регистрируют неправильный пароль. Identity защищает UPDATE через ConcurrencyStamp: один UPDATE проходит, второй возвращает ConcurrencyFailure. Приложение не замечает, что увеличение счётчика не сохранилось. Таким образом, число одновременных неправильных входов может превышать число учтённых попыток. Ограничение частоты по IP существует, но не гарантирует последовательность запросов к одной учётной записи. Это не утверждение об отсутствии lockout вообще.

Коррекция: проверять IdentityResult, обрабатывать конфликт ограниченным повтором с новым состоянием пользователя и повторной проверкой lockout. Успешный вход также не должен молча игнорировать неудачу сброса счётчика. Нужен конкурентный тест на реляционном provider.

Поведение библиотеки сверено независимой проверкой с исходниками именно используемой версии: [UserStore v10.0.9](https://github.com/dotnet/aspnetcore/blob/v10.0.9/src/Identity/EntityFrameworkCore/src/UserStore.cs), `UpdateAsync`; [UserManager v10.0.9](https://github.com/dotnet/aspnetcore/blob/v10.0.9/src/Identity/Extensions.Core/src/UserManager.cs), `AccessFailedAsync`, `UpdateUserAsync`.

### B6 — Minor: конфликт обновления профиля выдаётся за отсутствие пользователя

> Исправлено 2026-09-08: `UpdateDisplayNameAsync` различает отсутствие пользователя и `ConcurrencyFailure`; API возвращает `409 profile_update_conflict` для конфликта.

Источник: [UserAuthenticationService.cs](../src/GaifulinLab.Infrastructure/Authentication/UserAuthenticationService.cs), 101–108; [AuthController.cs](../src/GaifulinLab.Api/Controllers/AuthController.cs), 125–127.

`UpdateAsync(user).Succeeded == false` и отсутствие пользователя превращаются в один bool. При конкурентном обновлении существующего пользователя ConcurrencyFailure превращается в HTTP 404. Коррекция: различать NotFound, Conflict и ошибки Identity; для конфликта возвращать 409 или выполнять ограниченный корректный повтор. Основание по Identity — те же исходники, что в B5.

### B7 — Minor: bootstrap не выдерживает параллельный первый запуск

Источник: [TaxonomyBootstrapExtensions.cs](../src/GaifulinLab.Infrastructure/Persistence/TaxonomyBootstrapExtensions.cs), 31–55; [Program.cs](../src/GaifulinLab.Api/Program.cs), 75–77; [TopicLocalizationConfiguration.cs](../src/GaifulinLab.Infrastructure/Persistence/Configurations/TopicLocalizationConfiguration.cs), unique language/slug.

Два Development API с одной БД и одинаковым bootstrap одновременно проверяют отсутствие темы, затем оба вставляют её. Второй SaveChanges нарушает уникальный индекс, исключение останавливает startup. В текущем Program это Development-сценарий, а не обычный production startup. Коррекция: конкурентно безопасный seed с обработкой конкретного конфликта/повторным чтением или сериализацией. Имеющийся тест проверяет последовательный повтор, не параллельный.

## Возможности оптимизации

| Приоритет | Запросы | Что установлено | Рекомендация и ограничения |
| --- | --- | --- | --- |
| Высокий | GetPublicTopics, GetPublicTags, GetPublicSeries: 18–35 | По 3 запроса: все Id опубликованных статей → все связи → справочник. GroupBy/Count и финальная фильтрация выполняются после ToListAsync в памяти. Объём загрузки растёт с количеством статей и связей. | Оставить опубликованные статьи IQueryable; выполнить JOIN/EXISTS + GROUP BY/COUNT и проекцию DTO в БД. Сохранить язык, Published и DeletedAt. Основной кандидат для первой оптимизации. |
| Высокий | GetAdminArticle: 16–26; GetAdminTaxonomy: 26–31; UpdateArticleTaxonomy: 18–25 | Несколько Include коллекций одного уровня. В конфигурации не задан SplitQuery. Строки могут размножаться: например 2 локализации × 10 тем × 20 тегов = 400 строк с повторением текстов одной статьи. | Узкая проекция или отдельные/разделённые запросы. Для чтения админской статьи не нужен SearchText и поисковые vectors. Не включать SplitQuery глобально без оценки round trips и согласованности чтения. |
| Средний | GetAdminTaxonomy: 15–35, 65–69 | Загружаются все series links, затем фильтруются по массиву всех Id статей автора в памяти. Две коллекции Include также размножают строки. | Перенести ownership и DeletedAt в SQL-фильтр связей/проекцию; не перевозить посторонние связи. Текущий DTO уже фильтрует их: это избыточное чтение, не доказанная утечка в ответе. |
| Средний | GetPublicArticle: 24–35; UpdateArticleLocalization: 14–20; SetArticlePublication: 15–21 | Загрузка всех локализаций целиком, включая Markdown, SearchText и generated vectors. Публичной карточке нужен текст одного языка и короткий список остальных опубликованных языков. | Для публичного чтения проектировать нужный язык и короткие метаданные переводов. Для команд проверить возможность загрузки только целевой локализации, сохранив доменные инварианты добавления/версий. |
| Средний | IdentityAuthorDisplayNameLookup: 23–26 | ToDictionaryAsync с Func selectors не является SQL-проекцией: читается весь ApplicationUser, хотя требуются Id/DisplayName. | Добавить Select до ToDictionaryAsync. Это уменьшает данные и исключает ненужное чтение credential fields; выдача этих полей наружу не обнаружена. [EF Core v10.0.9, реализация ToDictionaryAsync](https://github.com/dotnet/efcore/blob/v10.0.9/src/EFCore/Extensions/EntityFrameworkQueryableExtensions.cs). |
| Средний | AdminPdfExportController: 107–116; PdfExportWorker: 104, 124 | Polling статуса и завершающие чтения поднимают всю PDF-задачу, включая Markdown. | Polling/download проектировать в короткий результат; завершение сделать условным UPDATE одновременно с исправлением B3. При создании экспорта использовать AsNoTracking/проекцию исходной локализации. |
| Средний при росте | GetAdminArticles: 15–36; GetPublicSeriesDetails: 28–49 | Списки не имеют пагинации. Админский список уже проектирует только summary, но возвращает все статьи автора; серия — все опубликованные статьи серии. | Добавлять пагинацию при подтверждённом росте; это изменение контракта/UI. Индекс `(OwnerUserId, DeletedAt, UpdatedAt)` уже есть. Для стабильной постраничной сортировки добавить Id как tie-breaker. |
| Средний при больших сериях | DeleteArticle: 23–26; UpdateArticleTaxonomy: 38–42 | Загружаются все участники затронутых серий. Для удаления одной связи это избыточно; для SetArticle коллекция сейчас используется доменом для проверки занятой позиции. | Удаление можно сузить до нужных связей и обновления времени серии. Для назначения проверить наличие целевой связи и занятую позицию, сохраняя уникальный индекс и защиту конкурентного изменения. Не убирать Include без адаптации доменных проверок. |
| Средний при большом трафике | GetPublicArticle: 41–42; PublicArticlesController: 117–118 | Два COUNT по просмотрам в обычном открытии: GET статьи и затем POST /views. PK начинается с ArticleId, но точный COUNT всё равно зависит от числа просмотров статьи. | Сначала измерить нагрузку. Возможны сокращение повторного счёта, короткий кэш или транзакционная агрегация. Не менять дедупликацию `(ArticleId, VisitorHash)` и не добавлять второй несогласованный счётчик. |
| Средний при миграционном объёме | DatabaseMigrationExtensions: 25–35 | Пакет из 100 SELECT-строк, но отдельный ExecuteUpdate на каждую. Поиск SearchText IS NULL повторяется; специального индекса нет. Backfill вызывается до старта API, включая проверку отсутствия работы при следующих стартах. | Рассмотреть пакетную запись и частичный индекс для незаполненных строк, если объём оправдывает его стоимость. Сохранить проверку Version; проверять необходимость индекса по плану. |
| Низкий | TaxonomyBootstrapExtensions: 33 | По одному EXISTS на каждую настройку темы. | Получать существующие ключи пакетом; отдельно исправить B7. Для нескольких тем экономия мала. |
| Только после замеров | ArticleSearch: 56–124 | Отдельные COUNT и страница; несколько полнотекстовых OR/EXISTS, ранжирование, ts_headline; глубокий OFFSET. GIN и индекс публичного списка уже существуют. | Снять EXPLAIN (ANALYZE, BUFFERS) для реальных all/title/content/topics/tags и селективностей. При доказанном плохом плане рассмотреть объединение множеств Id, повторное использование ранга, сокращение дорогих headline вычислений, keyset для подходящих сценариев. Не объявлять вложенный EXISTS или 6 запросов сами по себе багом. |

Отдельная низкоприоритетная возможность: ResolveTags читает и затем вставляет новый общий тег. При одновременном создании одинакового тега другой статьёй одна операция корректно получит 409 благодаря unique index и ApiExceptionFilter. Целостность сохранена; повторное чтение/повтор операции могло бы сделать этот сценарий удобнее. Аналогичные проверки slug защищены уникальным индексом: утверждать, что они позволяют дубликаты, неверно.

## Полный реестр прикладных чтений

Ссылки ведут на исходный LINQ/SQL; номера в колонке обозначают строку терминальной операции в проверенной версии. Одной строкой таблицы могут быть перечислены несколько разных запросов. «Без найденного дефекта» не означает измеренную производительность.

| Файл | Строки и все выполняемые чтения | Оценка |
| --- | --- | --- |
| [CreateArticleCommandHandler](../src/GaifulinLab.Application/Articles/CreateArticle/CreateArticleCommandHandler.cs) | 28: EXISTS MediaAsset по Id и image content type; 52: EXISTS локализации по language/slug, если slug задан | PK и unique slug; корректная защита конфликта на БД |
| [GetAdminArticleQueryHandler](../src/GaifulinLab.Application/Articles/GetAdminArticle/GetAdminArticleQueryHandler.cs) | 22: своя неудалённая статья + Localizations/Topics/Tags; 35: имена тегов по Id; 41: назначения серий по ArticleId | Оптимизация Include |
| [GetAdminArticlesQueryHandler](../src/GaifulinLab.Application/Articles/GetAdminArticles/GetAdminArticlesQueryHandler.cs) | 36: все свои неудалённые статьи, UpdatedAt DESC, summary локализаций | Нет пагинации; узкая проекция уже есть |
| [UpdateArticleLocalizationCommandHandler](../src/GaifulinLab.Application/Articles/UpdateArticleLocalization/UpdateArticleLocalizationCommandHandler.cs) | 16: своя неудалённая статья с локализациями; 68: EXISTS обложки; 77: EXISTS другого language/slug | Version локализации защищён; избыточная загрузка переводов; общая гонка с delete |
| [SetArticlePublicationCommandHandler](../src/GaifulinLab.Application/Articles/SetArticlePublication/SetArticlePublicationCommandHandler.cs) | 17: своя неудалённая статья с локализациями | Избыточная загрузка переводов; общая гонка с delete |
| [UpdateArticleTaxonomyCommandHandler](../src/GaifulinLab.Application/Articles/UpdateArticleTaxonomy/UpdateArticleTaxonomyCommandHandler.cs) | 21: своя неудалённая статья + темы/теги; 31: запрошенные Topics; 42: новые/прежние Series со всеми участниками; 80: Tags по normalized names | B1/B2; Include; широкая загрузка серий |
| [DeleteArticleCommandHandler](../src/GaifulinLab.Application/Articles/DeleteArticle/DeleteArticleCommandHandler.cs) | 15: своя неудалённая статья; 26: содержащие её Series со всеми участниками | B2; широкая загрузка серий |
| [GetPublicArticlesQueryHandler](../src/GaifulinLab.Application/Articles/Public/GetPublicArticles/GetPublicArticlesQueryHandler.cs) | 83: страница опубликованных локализаций, язык, неудалённая статья; optional topic/series/tag через EXISTS; PublishedAt DESC, Id DESC | B4; пагинация и проекция уже выполняются в БД |
| [GetPublicArticleQueryHandler](../src/GaifulinLab.Application/Articles/Public/GetPublicArticle/GetPublicArticleQueryHandler.cs) | 27: неудалённая статья с опубликованным language/slug и всеми переводами; 42: COUNT просмотров по ArticleId | Лишние тексты переводов; стоимость COUNT |
| [PublicArticleTaxonomyLoader](../src/GaifulinLab.Application/Articles/Public/PublicArticleTaxonomyLoader.cs) | 35: ArticleTopics JOIN TopicLocalizations по ArticleIds и языку; 52: ArticleSeries JOIN SeriesLocalizations; 62: ArticleTags JOIN Tags | 3 пакетных запроса после пагинации; не N+1. Пустые ArticleIds обходят БД |
| [ArticleSearch](../src/GaifulinLab.Infrastructure/Content/ArticleSearch.cs) | 77: COUNT всех совпадений; 124: ранжированная/отсортированная страница с snippet/headline | Требуются реальные планы; COUNT и страница не имеют общего snapshot |
| [GetAdminTaxonomyQueryHandler](../src/GaifulinLab.Application/Taxonomy/GetAdminTaxonomy/GetAdminTaxonomyQueryHandler.cs) | 19: Id всех своих неудалённых статей; 25: все Topics с переводами; 31: все Series с переводами и связями; 35: все Tags | Фильтрация ownership связей в памяти; Include; все справочники |
| [GetPublicTopicsQueryHandler](../src/GaifulinLab.Application/Taxonomy/Public/GetPublicTopics/GetPublicTopicsQueryHandler.cs) | 24: Id всех опубликованных неудалённых статей языка; 28: их ArticleTopics; 34: все TopicLocalizations языка | Перенести подсчёты в SQL |
| [GetPublicTagsQueryHandler](../src/GaifulinLab.Application/Taxonomy/Public/GetPublicTags/GetPublicTagsQueryHandler.cs) | 24: Id всех опубликованных неудалённых статей языка; 28: их ArticleTags; 35: Tags с ненулевым count | Перенести подсчёты в SQL |
| [GetPublicSeriesQueryHandler](../src/GaifulinLab.Application/Taxonomy/Public/GetPublicSeries/GetPublicSeriesQueryHandler.cs) | 24: Id всех опубликованных неудалённых статей языка; 28: их ArticleSeries; 34: все SeriesLocalizations языка | Перенести подсчёты в SQL |
| [GetPublicSeriesDetailsQueryHandler](../src/GaifulinLab.Application/Taxonomy/Public/GetPublicSeriesDetails/GetPublicSeriesDetailsQueryHandler.cs) | 25: SeriesLocalization по language/slug; 49: ArticleSeries JOIN опубликованные локализации неудалённых статей, Position ASC | Проекция и фильтры на сервере; возможна пагинация |
| [GetMediaFileQueryHandler](../src/GaifulinLab.Application/Media/GetMediaFile/GetMediaFileQueryHandler.cs) | 17: MediaAsset по PK, без tracking | Без найденного дефекта; можно сузить поля |
| [PublicArticlesController.RecordArticleView](../src/GaifulinLab.Api/Controllers/PublicArticlesController.cs) | 85: Id опубликованной неудалённой статьи по language/slug; 106: EXISTS ArticleView, **только InMemory fallback**; 118: COUNT ArticleView | Production INSERT атомарен; стоимость повторного COUNT |
| [AdminPdfExportController](../src/GaifulinLab.Api/Controllers/AdminPdfExportController.cs) | 41: опубликованная локализация неудалённой статьи для snapshot; 116: job по Id + EXISTS всё ещё опубликованной неудалённой локализации, общий для status/download | Сузить поля, особенно polling |
| [PdfExportWorker](../src/GaifulinLab.Infrastructure/Pdf/PdfExportWorker.cs) | 66: SELECT * очередной/просроченной job, CreatedAt, FOR UPDATE SKIP LOCKED LIMIT 1; 104: job по Id для завершения; 124: job по Id для ошибки | Захват транзакционный; завершение/ошибка B3 |
| [PlaywrightArticlePdfRenderer](../src/GaifulinLab.Infrastructure/Pdf/PlaywrightArticlePdfRenderer.cs) | 310: MediaAssets по множеству Id картинок для PDF | Один пакетный запрос, не DB N+1; можно проектировать Id/Size/RelativePath/ContentType |
| [IdentityAuthorDisplayNameLookup](../src/GaifulinLab.Infrastructure/Authentication/IdentityAuthorDisplayNameLookup.cs) | 26: Users по distinct Id → словарь отображаемых имён | Нужен Select до ToDictionaryAsync |
| [TaxonomyBootstrapExtensions](../src/GaifulinLab.Infrastructure/Persistence/TaxonomyBootstrapExtensions.cs) | 33: EXISTS TopicLocalization language/slug в цикле | B7; N запросов для N настроек |
| [DatabaseMigrationExtensions.BackfillArticleSearchAsync](../src/GaifulinLab.Infrastructure/Persistence/DatabaseMigrationExtensions.cs) | 27: первые 100 локализаций SearchText IS NULL, Id ASC, только Id/Version/Markdown | Цикл до завершения; пакетная оптимизация |

Общие загрузчики не продублированы в счёте исходных точек. Для непустых результатов: публичный список обычно выполняет 5 чтений (страница + 3 таксономии + авторы), публичная статья — 6 (статья + 3 таксономии + COUNT + авторы), поиск — 6 (COUNT + страница + 3 таксономии + авторы), подробности серии — 3 (серия + статьи + авторы). Число не растёт на один запрос с каждой статьёй.

## Записи, Identity и служебные обращения

| Источник | Операции |
| --- | --- |
| CreateArticleCommandHandler:38 | SaveChanges: INSERT Article и ArticleLocalization; derived search text готовится в AppDbContext |
| UpdateArticleLocalizationCommandHandler:91 | SaveChanges: UPDATE Article, INSERT/UPDATE локализации; concurrency Version для существующей локализации |
| SetArticlePublicationCommandHandler:43 | SaveChanges: UPDATE публикации/версии локализации и времени Article |
| UpdateArticleTaxonomyCommandHandler:66 | SaveChanges: INSERT/DELETE тем и тегов статьи; INSERT новых общих Tags; INSERT/UPDATE/DELETE ArticleSeries; UPDATE времён Article/Series |
| DeleteArticleCommandHandler:34 | SaveChanges: UPDATE Article.DeletedAt/UpdatedAt; DELETE его ArticleSeries и UPDATE времён затронутых Series |
| [UploadMediaCommandHandler:38](../src/GaifulinLab.Application/Media/UploadMedia/UploadMediaCommandHandler.cs) | SaveChanges: INSERT MediaAsset после сохранения файла. При исключении есть компенсационное удаление файла; полного атомарного commit БД+файл нет |
| PublicArticlesController:100,113 | Production: параметризованный INSERT ArticleView ON CONFLICT DO NOTHING; отдельный SaveChanges INSERT только в InMemory-ветке |
| AdminPdfExportController:56 | SaveChanges: INSERT PdfExportJob со snapshot текста |
| PdfExportWorker:74,106,131 | Три SaveChanges: захват/lease/AttemptCount; Completed/path/size; Failed/error |
| TaxonomyBootstrapExtensions:55 | Один SaveChanges для всех отсутствующих Topics и их Localizations |
| DatabaseMigrationExtensions:34 | ExecuteUpdate по Id+Version: SearchText, ReadingMinutes; один UPDATE на строку |
| [UserAuthenticationService](../src/GaifulinLab.Infrastructure/Authentication/UserAuthenticationService.cs):18,24–30,41 | FindByNameAsync читает пользователя; CheckPasswordAsync может обновить hash при необходимости rehash; AccessFailedAsync/ResetAccessFailedCountAsync могут писать пользователя; GetRolesAsync читает роли через membership. IsLockedOutAsync работает с загруженным user, отдельный SQL SELECT не предполагается |
| UserAuthenticationService:65 | CreateAsync: Identity-валидация и проверка уникальности имени, INSERT пользователя; число внутренних запросов зависит от validator/store |
| UserAuthenticationService:93,101,108 | FindByIdAsync для GET профиля; FindByIdAsync и UpdateAsync с Identity-валидацией/ConcurrencyStamp для PUT |
| [HealthController:25](../src/GaifulinLab.Api/Controllers/HealthController.cs) | Database.CanConnectAsync: проверка доступности БД, не бизнес-выборка |
| DatabaseMigrationExtensions:16 | Database.MigrateAsync: история миграций и DDL/DML. В текущем src вызов ApplyDatabaseMigrationsAsync извне не найден; deployment использует dotnet-ef database update |

`AppDbContext.PrepareSearchText` работает с ChangeTracker и не выполняет скрытых SELECT. Генерируемые поисковые vectors вычисляются PostgreSQL при записи. Lazy-loading proxies и глобальные query filters в конфигурации не обнаружены; проверка DeletedAt выполняется явно в конкретных запросах. Отсутствие глобального фильтра само по себе не классифицировано как ошибка.

## Миграционные запросы

Каталог: [Persistence/Migrations](../src/GaifulinLab.Infrastructure/Persistence/Migrations). Designer и ModelSnapshot — описание модели, а не отдельные runtime-запросы. Все 12 миграций учтены:

| Миграция | Назначение SQL/операций EF |
| --- | --- |
| 20260830003348_InitialCreate | Создание таблиц контента, связей, PK/FK/check/unique indexes; Down удаляет созданную схему |
| 20260901000000_AddPdfExportJobs | Таблица PDF jobs, ограничения статуса/попыток, индексы очереди и lease |
| 20260902143458_AddAspNetCoreIdentity | Таблицы Identity, индексы имени пользователя/ролей, связи |
| 20260906210330_AddArticleViews | Таблица просмотров, PK ArticleId+VisitorHash и FK |
| 20260906214346_AddArticleOwnership | Добавление владельца; DO-блок: EXISTS статей, COUNT/MIN Admin, UPDATE OwnerUserId; отказ при неоднозначном владельце; затем NOT NULL/FK/index |
| 20260906225610_AddArticleLifecycle | Статусы/soft delete/LastEditedAt и ограничения; UPDATE LastEditedAt из UpdatedAt для NULL; индекс владельца с DeletedAt |
| 20260907142809_AddUserDisplayName | Добавление DisplayName |
| 20260907200059_ReleaseDeletedArticleSeriesLinks | DELETE article_series USING articles WHERE DeletedAt IS NOT NULL; Down не восстанавливает удалённые связи |
| 20260907210000_AddArticleLocalizationVersion | Добавление Version |
| 20260908000000_OptimizePublicArticleListingPagination | Перестройка индекса списка с Id для стабильного порядка |
| 20260908120000_AddArticleSearchAndCovers | SearchText/ReadingMinutes/CoverMediaAssetId, FK; функции gl_search_config/normalize/vector и GIN expression indexes |
| 20260908130000_UseEntityFrameworkArticleSearch | Замена старых функций/индексов generated tsvector columns и GIN indexes; Down восстанавливает прежние функции/индексы |

Операционные скрипты применения миграций вызывают EF CLI; резервное копирование использует pg_dump. Они не добавляют самостоятельных запросов чтения контента приложения. SQL тестовых фикстур, seed и EXPLAIN в tests рассматривался как проверочная инфраструктура, а не production-нагрузка.

## Уже существующая защита и открытые вопросы

- Slug локализации уникален по language/slug; язык статьи уникален по ArticleId/language. Коллизия конкурентных INSERT не создаёт дубликаты: PostgreSQL запрещает её, ApiExceptionFilter превращает UniqueViolation в 409.
- Position серии защищён unique `(SeriesId, Position)`; normalized tag name — unique. Эти индексы не защищают логическую замену всего множества в B1 или soft delete в B2.
- UPDATE локализации проверяет Version на сервере и в SQL через concurrency token. Это реальная защита редактирования текста, но не версия агрегата Article.
- Просмотр в production записывается параметризованным INSERT ON CONFLICT по составному PK. InMemory EXISTS+INSERT не доказывает его конкурентную корректность, но production-код применяет атомарный механизм.
- Публичные выборки контента проверяют Published и DeletedAt; собственность в командах/админской выдаче берётся из аутентифицированного user id. SQL-инъекция в исследованных запросах не выявлена.
- Сохранение slug за soft-deleted статьёй следует из query и unique index. Без утверждённого правила повторного использования slug это **продуктовый вопрос**, а не доказанный баг.
- Search COUNT и получение страницы — разные SQL statements без общего snapshot. При одновременной публикации/снятии возможны временные расхождения total/items. Требование snapshot-consistency не найдено; это ограничение, а не безусловная ошибка.
- PDF snapshot хранится независимо от дальнейшего редактирования, а download проверяет актуальную опубликованность исходной локализации. Политика устаревания/удаления старых jobs и файлов не определена; рост таблицы и файлов — повод уточнить retention, не самовольное основание их удалять.
- Тест ArticleSearchTests включает EXPLAIN с `enable_seqscan=off` для упрощённого запроса по BodySearchVector. Он подтверждает доступность индекса, но не хороший план полного production-поиска с OR, таксономией, COUNT, ranking и headline.

## Выполненные проверки

1. Поиск по всему src терминальных EF-операций, DbContext/Database, raw SQL и Identity; чтение всех найденных мест, relevant callers и ограничений модели/миграций. Независимый исследователь проверил Infrastructure/Identity/PDF/startup и дополнительно B1–B4.
2. `dotnet test tests/GaifulinLab.Api.Tests/GaifulinLab.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~AdminArticleEndpointsTests|FullyQualifiedName~PublicArticleEndpointsTests|FullyQualifiedName~PublicTaxonomyEndpointsTests|FullyQualifiedName~PublicSearchValidationTests" --verbosity quiet` — сборка Debug не прошла: MSB3027/MSB3021, DLL заняты уже работающим API. Тесты в этом запуске не выполнялись.
3. Та же команда с `-c Release` — **39 passed, 0 failed**.
4. `dotnet test tests/GaifulinLab.Infrastructure.Tests/GaifulinLab.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Persistence|FullyQualifiedName~ArticleSearchTextTests" --verbosity quiet` — **9 passed, 0 failed**.
5. `dotnet test tests/GaifulinLab.Api.Tests/GaifulinLab.Api.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~AdminPdfExportEndpointsTests|FullyQualifiedName~MediaEndpointsTests" --verbosity quiet` — **27 passed, 0 failed**.
6. Исполнение C# через PowerShell Add-Type: `unchecked((page - 1) * size)` для двух примеров B4 — получены отрицательное и нулевое смещения.

Итого: **75 прошедших тестов**, текущие production-проекты для них собраны в Release. API-тесты используют InMemory. Проверки модели генерируют/анализируют PostgreSQL metadata/DDL, но это не выполнение запросов на PostgreSQL. E2E не запускались: штатная фикстура применяет миграции и сохраняет данные в общей development-БД. PostgreSQL-конкуренция, нагрузочное тестирование, EXPLAIN ANALYZE и проверка схемы развёрнутой БД остаются невыполненными. Новые регрессионные тесты не добавлялись, исправления не запрашивались.

Рекомендуемый порядок работ: B1/B2/B3/B5 (целостность и concurrency), B4 (локальная ошибка пагинации), B6/B7; затем SQL-агрегация публичной таксономии и сокращение широких проекций/Include. Индексы поиска и очереди изменять после измерений полного запроса.
