using Microsoft.AspNetCore.Authorization;
using church.AIServices;
using church.Models;
using church.Models.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace church.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AIController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly context _db;
        private readonly IFirestoreSettingsService _settingsService;
        private readonly ISubscriptionCalculationService _subscriptionCalculator;

        public AIController(
            IHttpClientFactory httpClientFactory,
            context db,
            IFirestoreSettingsService settingsService,
            ISubscriptionCalculationService subscriptionCalculator)
        {
            _httpClientFactory = httpClientFactory;
            _db = db;
            _settingsService = settingsService;
            _subscriptionCalculator = subscriptionCalculator;
        }

        private const int AttendanceAbsentStatus = 0;
        private const int AttendancePresentStatus = 1;

        private static readonly ConcurrentDictionary<string, ChatSessionState>
            ChatSessions = new();

        private static readonly TimeSpan SessionLifetime =
            TimeSpan.FromHours(2);

        // =========================================================
        // STATIC SERVANT ROLES
        // =========================================================

        private static readonly Dictionary<int, string>
            ServantsRolesStatic = new()
            {
                { 8, "قادة الفريق" },
                { 9, "اعداد قادة" },
                { 1, "قائد الادريات" },
                { 0, "خدام الاداريات" },
                { 14, "ادريات ابتدائي" },

                { 7, "خدام مرحلة اعدادي" },
                { 10, "خدام مرحلة ثانوي" },
                { 20, "خدام مرحلة جامعة وخريجين" },

                { 168, "خادم ادوات اعدادي" },
                { 175, "خادم اشتراكات اعدادي" },
                { 210, "خادم انشطة اعدادي" },
                { 245, "خادم اداري اعدادي" },
                { 280, "خادم فني اعدادي" },
                { 315, "قائد اعدادي" },

                { 240, "خادم ادوات ثانوي" },
                { 250, "خادم اشتراكات ثانوي" },
                { 300, "خادم انشطة ثانوي" },
                { 350, "خادم اداري ثانوي" },
                { 400, "خادم فني ثانوي" },
                { 450, "قائد ثانوي" },

                { 480, "خادم ادوات جامعة وخريجين" },
                { 500, "خادم اشتراكات جامعة وخريجين" },
                { 600, "خادم انشطة جامعة وخريجين" },
                { 700, "خادم اداري جامعة وخريجين" },
                { 800, "خادم فني جامعة وخريجين" },
                { 900, "قائد جامعة وخريجين" },

                { 2, "الميديا" },
                { 3, "دراسة الكتاب" },
                { 4, "الجرافيك" },
                { 11, "الادلبة" },
                { 12, "الترابيزات" },
                { 13, "المكتب و الادوات" },
                { 5, "الهاند ميد" },
                { 6, "الاشتراكات" },
                { 16, "مشرف تقييمات" },
                { 40, "المخزن" },

                { 1001, "مشرف محوعة 1" },
                { 1002, "مشرف محوعة 2" },
                { 1003, "مشرف محوعة 3" },
                { 1004, "مشرف محوعة 4" },
                { 1005, "مشرف محوعة 5" },
                { 1006, "مشرف محوعة 6" },
                { 1007, "مشرف محوعة 7" },
                { 1008, "مشرف محوعة 8" }
            };

        private static readonly HashSet<string>
            AllowedSearchFields =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    "name",
                    "nameEnglish",
                    "phone",
                    "anotherPhone",
                    "address",
                    "area",
                    "location",
                    "notes",
                    "excused",
                    "role",
                    "details",
                    "roleId",
                    "dateOfBirth",
                    "gender",
                    "confessor",
                    "createdAt",
                    "createdBy",
                    "updatedBy"
                };

        // =========================================================
        // REQUEST
        // =========================================================

        public class ChatRequest
        {
            public string Message { get; set; } = "";

            public string? ConversationId { get; set; }

            // Flutter يرسلها تلقائياً عند ضغط زر الشخص
            public string? SelectedQr { get; set; }

            // Flutter يرسلها تلقائياً عند ضغط زر المرحلة
            public int? SelectedGradeId { get; set; }
        }

        // =========================================================
        // SESSION
        // =========================================================

        private class ChatSessionState
        {
            public PersonResult? SelectedPerson { get; set; }

            public List<PersonResult> PendingCandidates { get; set; } =
                new();

            public PersonAction? PendingPersonAction { get; set; }

            public GroupAction? PendingGroupAction { get; set; }

            public PersonAction? LastPersonAction { get; set; }

            public GroupAction? LastGroupAction { get; set; }

            public int? LastGradeId { get; set; }

            public DateTime UpdatedAtUtc { get; set; } =
                DateTime.UtcNow;
        }

        private class PersonAction
        {
            public string Kind { get; set; } = "summary";

            public DateTime? FromDate { get; set; }

            public DateTime? ToDate { get; set; }

            public bool AllTime { get; set; }

            public bool IncludeAudit { get; set; }

            public string PaymentStatus { get; set; } = "all";
        }

        private class GroupAction
        {
            public string Kind { get; set; } = "";

            public int? GradeId { get; set; }

            public DateTime? FromDate { get; set; }

            public DateTime? ToDate { get; set; }

            public bool AllTime { get; set; }

            public bool IncludeAudit { get; set; }

            public string PaymentStatus { get; set; } = "all";
        }

        // =========================================================
        // DATA MODELS
        // =========================================================

        public class GradeResult
        {
            public int Id { get; set; }

            public string Name { get; set; } = "";
        }

        public class PersonResult
        {
            public int? Id { get; set; }

            public string? Qr { get; set; }

            public string? Name { get; set; }

            public string? NameEnglish { get; set; }

            public int? Grade { get; set; }

            public string? Phone { get; set; }

            public string? AnotherPhone { get; set; }

            public string? Address { get; set; }

            public string? Area { get; set; }

            public string? Location { get; set; }

            public string? Notes { get; set; }

            public string? Excused { get; set; }

            public string? Role { get; set; }

            public string? Details { get; set; }

            public int? RoleId { get; set; }

            public string? DateOfBirth { get; set; }

            public int? Gender { get; set; }

            public string? Confessor { get; set; }

            public string? CreatedAt { get; set; }

            public string? CreatedBy { get; set; }

            public string? UpdatedBy { get; set; }
        }

        public class AttendanceRecord
        {
            public DateTime? Date { get; set; }

            public int? Status { get; set; }

            public string? Comment { get; set; }

            public string? Excused { get; set; }

            public DateTime? LastUpdated { get; set; }

            public string? UserName { get; set; }
        }

        public class SubscriptionRecord
        {
            public int? StudentId { get; set; }

            public string? StudentQr { get; set; }

            public string? StudentName { get; set; }

            public string? Excused { get; set; }

            public bool? IsPaid { get; set; }

            public int? SubscriptionId { get; set; }

            public DateTime? LastUpdated { get; set; }

            public string? UserName { get; set; }
        }

        public class VisitationRecord
        {
            public int? Id { get; set; }

            public int? StudentId { get; set; }

            public string? StudentQr { get; set; }

            public string? StudentName { get; set; }

            public string? Excused { get; set; }

            public DateTime? Date { get; set; }

            public string? Comment { get; set; }

            public string? VisitedBy { get; set; }
        }

        private class PersonAttendanceAggregate
        {
            public PersonResult Person { get; set; } = new();

            public List<AttendanceRecord> Records { get; set; } =
                new();
        }

        private class SubscriptionMonthResult
        {
            public int Month { get; set; }

            public int Year { get; set; }

            public List<SubscriptionRecord> Records { get; set; } =
                new();
        }

        private class GroupSubscriptionMemberReport
        {
            public PersonResult Person { get; set; } = new();

            public List<string> PaidMonths { get; set; } = new();

            public List<string> UnpaidMonths { get; set; } = new();

            public decimal? TotalDue { get; set; }

            public List<string> MissingPriceMonths { get; set; } = new();

            public bool RequiresEmploymentStatus { get; set; }
        }

        // =========================================================
        // MAIN CHAT
        // =========================================================

        [HttpPost("chat")]
        public async Task<IActionResult> Chat(
            [FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message) &&
                string.IsNullOrWhiteSpace(request.SelectedQr) &&
                !request.SelectedGradeId.HasValue)
            {
                return BadRequest(new
                {
                    message =
                        "Message, SelectedQr or SelectedGradeId is required"
                });
            }

            var groqApiKey =
                Environment.GetEnvironmentVariable(
                    "GROQ_API_KEY"
                );

            if (string.IsNullOrWhiteSpace(groqApiKey))
            {
                return StatusCode(500, new
                {
                    message =
                        "GROQ_API_KEY is not configured"
                });
            }

            CleanupOldSessions();

            var authorization =
                Request.Headers.Authorization.ToString();

            var conversationId =
                GetOrCreateConversationId(
                    request.ConversationId
                );

            var sessionKey =
                BuildSessionKey(
                    conversationId,
                    authorization
                );

            var session =
                ChatSessions.GetOrAdd(
                    sessionKey,
                    _ => new ChatSessionState()
                );

            TouchSession(session);

            var message =
                request.Message?.Trim() ?? "";

            var egyptNow =
                GetEgyptNow().Date;

            // =====================================================
            // LOAD CURRENT GRADES
            // ALWAYS DYNAMIC
            // =====================================================

            var gradesResult =
                await GetGrades(
                    authorization
                );

            if (!gradesResult.Success)
            {
                return StatusCode(
                    gradesResult.StatusCode,
                    new
                    {
                        conversationId,
                        message =
                            gradesResult.ErrorMessage
                    }
                );
            }

            var grades =
                gradesResult.Grades;

            // =====================================================
            // GRADE BUTTON CLICK
            // =====================================================

            if (request.SelectedGradeId.HasValue)
            {
                var grade =
                    grades.FirstOrDefault(
                        x =>
                            x.Id ==
                            request.SelectedGradeId.Value
                    );

                if (grade == null)
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "المرحلة المختارة غير موجودة حالياً."
                    });
                }

                session.LastGradeId =
                    grade.Id;

                TouchSession(session);

                if (session.PendingGroupAction != null)
                {
                    var pending =
                        CloneGroupAction(
                            session.PendingGroupAction
                        );

                    pending.GradeId =
                        grade.Id;

                    session.PendingGroupAction =
                        null;

                    return await ExecuteGroupAction(
                        pending,
                        session,
                        grades,
                        authorization,
                        conversationId
                    );
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    return Ok(new
                    {
                        conversationId,

                        type =
                            "grade_selected",

                        answer =
                            $"تمام، تم اختيار {grade.Name}.",

                        grade = new
                        {
                            id = grade.Id,
                            name = grade.Name
                        }
                    });
                }
            }

            // =====================================================
            // PERSON BUTTON CLICK
            // =====================================================

            if (!string.IsNullOrWhiteSpace(
                request.SelectedQr))
            {
                var pendingAction =
                    session.PendingPersonAction;

                var selected =
                    session.PendingCandidates
                        .FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.Qr,
                                    request.SelectedQr,
                                    StringComparison.OrdinalIgnoreCase
                                )
                        );

                if (selected == null)
                {
                    selected =
                        await GetPersonForSelectionByQr(
                            request.SelectedQr!,
                            authorization
                        );
                }

                if (selected == null)
                {
                    return BadRequest(new
                    {
                        conversationId,

                        message =
                            "لم أتمكن من العثور على الشخص المختار أو ليس لديك صلاحية لعرضه."
                    });
                }

                SetSelectedPerson(
                    session,
                    selected
                );

                if (pendingAction != null)
                {
                    session.PendingPersonAction =
                        null;

                    return await ExecutePersonAction(
                        pendingAction,
                        session,
                        grades,
                        authorization,
                        conversationId
                    );
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    var inferred =
                        InferPersonActionFromMessage(
                            message,
                            egyptNow,
                            session
                        );

                    if (inferred != null)
                    {
                        return await ExecutePersonAction(
                            inferred,
                            session,
                            grades,
                            authorization,
                            conversationId
                        );
                    }
                }

                return Ok(new
                {
                    conversationId,

                    type =
                        "person_selected",

                    answer =
                        $"تمام، تم اختيار {selected.Name}.",

                    selectedPerson =
                        ToBasicPerson(selected),

                    actions =
                        SelectedPersonActions()
                });
            }

            // =====================================================
            // USER TYPES PERSON CHOICE:
            // 1 / الأول / QR / exact name
            // =====================================================

            if (session.PendingCandidates.Count > 0 &&
                !string.IsNullOrWhiteSpace(message))
            {
                var selected =
                    ResolvePendingSelection(
                        message,
                        session.PendingCandidates
                    );

                if (selected != null)
                {
                    var pendingAction =
                        session.PendingPersonAction;

                    SetSelectedPerson(
                        session,
                        selected
                    );

                    session.PendingPersonAction =
                        null;

                    if (pendingAction != null)
                    {
                        return await ExecutePersonAction(
                            pendingAction,
                            session,
                            grades,
                            authorization,
                            conversationId
                        );
                    }

                    return Ok(new
                    {
                        conversationId,

                        type =
                            "person_selected",

                        answer =
                            $"تمام، تقصد {selected.Name}.",

                        selectedPerson =
                            ToBasicPerson(selected),

                        actions =
                            SelectedPersonActions()
                    });
                }
            }

            // =====================================================
            // WAITING FOR PERIOD FOR A GROUP QUERY
            // =====================================================

            if (session.PendingGroupAction != null &&
                !string.IsNullOrWhiteSpace(message))
            {
                var pending =
                    CloneGroupAction(
                        session.PendingGroupAction
                    );

                var changed =
                    false;

                if (TryResolveDateRangeFromMessage(
                        message,
                        egyptNow,
                        out var fromDate,
                        out var toDate,
                        out var allTime))
                {
                    pending.FromDate =
                        fromDate;

                    pending.ToDate =
                        toDate;

                    pending.AllTime =
                        allTime;

                    changed =
                        true;
                }

                if (!pending.GradeId.HasValue &&
                    TryResolveGradeFromMessage(
                        message,
                        grades,
                        out var detectedGrade))
                {
                    pending.GradeId =
                        detectedGrade;

                    changed =
                        true;
                }

                if (changed)
                {
                    session.PendingGroupAction =
                        pending;

                    return await ExecuteGroupAction(
                        pending,
                        session,
                        grades,
                        authorization,
                        conversationId
                    );
                }
            }

            // =====================================================
            // WAITING FOR PERIOD FOR A PERSON QUERY
            // =====================================================

            if (session.SelectedPerson != null &&
                session.PendingPersonAction != null &&
                session.PendingCandidates.Count == 0 &&
                !string.IsNullOrWhiteSpace(message))
            {
                if (TryResolveDateRangeFromMessage(
                        message,
                        egyptNow,
                        out var fromDate,
                        out var toDate,
                        out var allTime))
                {
                    var pending =
                        ClonePersonAction(
                            session.PendingPersonAction
                        );

                    pending.FromDate =
                        fromDate;

                    pending.ToDate =
                        toDate;

                    pending.AllTime =
                        allTime;

                    session.PendingPersonAction =
                        null;

                    return await ExecutePersonAction(
                        pending,
                        session,
                        grades,
                        authorization,
                        conversationId
                    );
                }
            }

            // =====================================================
            // DETERMINISTIC ROUTING FOR COMMON ARABIC REQUESTS
            //
            // Do not send obvious attendance/subscription requests to
            // the model first.  The model is useful for ambiguous
            // requests, but the backend already knows how to resolve
            // the common intents and periods exactly.
            // =====================================================

            if (session.SelectedPerson == null &&
                TryExtractPersonRequest(
                    message,
                    egyptNow,
                    session,
                    out var personQuery,
                    out var personAction))
            {
                var personMatches = await FindPeopleByName(
                    personQuery,
                    grades,
                    authorization
                );

                if (personMatches.Count == 1)
                {
                    SetSelectedPerson(session, personMatches[0]);
                    return await ExecutePersonAction(
                        personAction,
                        session,
                        grades,
                        authorization,
                        conversationId
                    );
                }

                if (personMatches.Count > 1)
                {
                    return PersonAmbiguousResponse(
                        personQuery,
                        personMatches,
                        grades,
                        personAction,
                        session,
                        conversationId
                    );
                }

                if (!HasExplicitGroupScope(message, grades))
                {
                    return Ok(new
                    {
                        conversationId,
                        type = "person_not_found",
                        answer = $"ملقتش شخص باسم «{personQuery}» في المراحل المسموح لك بها. لو تقصد مجموعة، اكتب اسم المرحلة أو قل مثلًا: طلاب المرحلة الإعدادية."
                    });
                }
            }

            if (session.SelectedPerson == null &&
                TryBuildDeterministicGroupAction(
                    message,
                    grades,
                    egyptNow,
                    out var directGroupAction))
            {
                if (!directGroupAction.GradeId.HasValue)
                {
                    return AskForGrade(
                        directGroupAction,
                        session,
                        grades,
                        conversationId
                    );
                }

                return await ExecuteGroupAction(
                    directGroupAction,
                    session,
                    grades,
                    authorization,
                    conversationId
                );
            }

            if (session.SelectedPerson != null &&
                !HasExplicitGroupScope(message, grades))
            {
                var directPersonAction =
                    InferPersonActionFromMessage(
                        message,
                        egyptNow,
                        session
                    );

                if (directPersonAction != null)
                {
                    return await ExecutePersonAction(
                        directPersonAction,
                        session,
                        grades,
                        authorization,
                        conversationId
                    );
                }
            }

            // =====================================================
            // PREPARE AI TOOLS
            // =====================================================

            var gradeIds =
                grades
                    .Select(x => x.Id)
                    .ToArray();

            var roleIds =
                ServantsRolesStatic
                    .Keys
                    .ToArray();

            var gradesText =
                string.Join(
                    "\n",
                    grades.Select(
                        x =>
                            $"{x.Id} = {x.Name}"
                    )
                );

            var rolesText =
                string.Join(
                    "\n",
                    ServantsRolesStatic.Select(
                        x =>
                            $"{x.Key} = {x.Value}"
                    )
                );

            var tools =
                BuildTools(
                    gradeIds,
                    roleIds
                );

            var systemPrompt =
                BuildSystemPrompt(
                    session,
                    gradesText,
                    rolesText,
                    egyptNow
                );

            var messages =
                new List<object>
                {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },

                    new
                    {
                        role = "user",
                        content = message
                    }
                };

            var requireTool =
                LooksLikeDataRequest(
                    message
                );

            var groqResponse =
                await SendToGroq(
                    groqApiKey,
                    messages,
                    tools,
                    requireTool
                        ? "required"
                        : "auto"
                );

            if (!groqResponse.Success &&
                requireTool)
            {
                groqResponse =
                    await SendToGroq(
                        groqApiKey,
                        messages,
                        tools,
                        "auto"
                    );
            }

            if (!groqResponse.Success)
            {
                return StatusCode(
                    groqResponse.StatusCode,
                    new
                    {
                        conversationId,

                        message =
                            "Groq request failed",

                        details =
                            groqResponse.Raw
                    }
                );
            }

            using var groqDocument =
                JsonDocument.Parse(
                    groqResponse.Raw
                );

            var aiMessage =
                groqDocument
                    .RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message");

            if (!aiMessage.TryGetProperty(
                    "tool_calls",
                    out var toolCalls)
                ||
                toolCalls.ValueKind !=
                    JsonValueKind.Array
                ||
                toolCalls.GetArrayLength() == 0)
            {
                string? answer =
                    null;

                if (aiMessage.TryGetProperty(
                        "content",
                        out var contentElement)
                    &&
                    contentElement.ValueKind ==
                        JsonValueKind.String)
                {
                    answer =
                        contentElement.GetString();
                }

                if (string.IsNullOrWhiteSpace(answer))
                {
                    answer =
                        "كيف أقدر أساعدك؟";
                }

                return Ok(new
                {
                    conversationId,

                    type =
                        "message",

                    answer
                });
            }

            var toolCall =
                toolCalls[0];

            var function =
                toolCall
                    .GetProperty("function");

            var functionName =
                function
                    .GetProperty("name")
                    .GetString();

            TryGetArguments(
                function,
                out var args
            );

            // =====================================================
            // SIMPLE READ TOOLS
            // =====================================================

            if (functionName ==
                "get_grades")
            {
                return Ok(new
                {
                    conversationId,

                    type =
                        "grades_list",

                    count =
                        grades.Count,

                    answer =
                        BuildGradesAnswer(
                            grades
                        ),

                    data =
                        grades
                });
            }

            if (functionName ==
                "get_churches")
            {
                var api =
                    await GetExactLocal(
                        "/api/Churches/show",
                        authorization
                    );

                return ExactApiResponse(
                    "churches",
                    "الكنائس",
                    api,
                    conversationId
                );
            }

            if (functionName ==
                "get_services")
            {
                var api =
                    await GetExactLocal(
                        "/api/Services/show",
                        authorization
                    );

                return ExactApiResponse(
                    "services",
                    "الخدمات",
                    api,
                    conversationId
                );
            }

            if (functionName ==
                "get_church_services")
            {
                if (!TryGetInt(
                        args,
                        "church_id",
                        out var churchId))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "تعذر تحديد الكنيسة."
                    });
                }

                var api =
                    await GetExactLocal(
                        $"/api/Churches/{churchId}/services",
                        authorization
                    );

                return ExactApiResponse(
                    "church_services",
                    "خدمات الكنيسة",
                    api,
                    conversationId
                );
            }

            // =====================================================
            // PEOPLE BY GRADE
            // =====================================================

            if (functionName ==
                "get_students_by_grade")
            {
                if (!TryGetInt(
                        args,
                        "grade_id",
                        out var gradeId))
                {
                    return AskForGrade(
                        new GroupAction
                        {
                            Kind =
                                "people"
                        },
                        session,
                        grades,
                        conversationId
                    );
                }

                var grade =
                    grades.FirstOrDefault(
                        x =>
                            x.Id ==
                            gradeId
                    );

                if (grade == null)
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "المرحلة غير موجودة."
                    });
                }

                var peopleResult =
                    await GetPeopleByGrade(
                        grade.Id,
                        authorization
                    );

                if (!peopleResult.Success)
                {
                    return ApiError(
                        peopleResult,
                        conversationId
                    );
                }

                session.LastGradeId =
                    grade.Id;

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        "people_list",

                    grade = new
                    {
                        id =
                            grade.Id,

                        name =
                            grade.Name
                    },

                    count =
                        peopleResult.People.Count,

                    answer =
                        BuildPeopleAnswer(
                            grade,
                            peopleResult.People
                        ),

                    data =
                        peopleResult.People
                            .Select(
                                ToBasicPerson
                            )
                });
            }

            // =====================================================
            // PERSON FIND
            // =====================================================

            if (functionName ==
                "find_person_by_name")
            {
                if (!TryGetString(
                        args,
                        "name",
                        out var name))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "تعذر تحديد الاسم."
                    });
                }

                var action =
                    InferPersonActionFromMessage(
                        message,
                        egyptNow,
                        session
                    )
                    ??
                    new PersonAction
                    {
                        Kind =
                            "summary"
                    };

                return await ResolvePersonAndExecute(
                    name!,
                    action,
                    session,
                    grades,
                    authorization,
                    conversationId
                );
            }

            // =====================================================
            // PERSON TOOLS BY NAME
            // =====================================================

            if (IsPersonByNameTool(
                functionName))
            {
                if (!TryGetString(
                        args,
                        "name",
                        out var name))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "تعذر تحديد الاسم."
                    });
                }

                var action =
                    BuildPersonActionFromTool(
                        functionName!,
                        args,
                        message,
                        egyptNow,
                        session
                    );

                return await ResolvePersonAndExecute(
                    name!,
                    action,
                    session,
                    grades,
                    authorization,
                    conversationId
                );
            }

            // =====================================================
            // SELECTED PERSON TOOLS
            // =====================================================

            if (IsSelectedPersonTool(
                functionName))
            {
                if (session.SelectedPerson == null)
                {
                    return Ok(new
                    {
                        conversationId,

                        type =
                            "person_required",

                        answer =
                            "حدد الشخص المقصود أولاً."
                    });
                }

                var action =
                    BuildPersonActionFromTool(
                        functionName!,
                        args,
                        message,
                        egyptNow,
                        session
                    );

                return await ExecutePersonAction(
                    action,
                    session,
                    grades,
                    authorization,
                    conversationId
                );
            }

            // =====================================================
            // GROUP TOOLS
            // =====================================================

            if (IsGroupTool(
                functionName))
            {
                var action =
                    BuildGroupActionFromTool(
                        functionName!,
                        args,
                        message,
                        egyptNow
                    );

                if (!action.GradeId.HasValue)
                {
                    return AskForGrade(
                        action,
                        session,
                        grades,
                        conversationId
                    );
                }

                return await ExecuteGroupAction(
                    action,
                    session,
                    grades,
                    authorization,
                    conversationId
                );
            }

            // =====================================================
            // SERVANT ROLES
            // =====================================================

            if (functionName ==
                "get_servant_roles")
            {
                var roles =
                    ServantsRolesStatic
                        .Select(
                            x => new
                            {
                                id =
                                    x.Key,

                                name =
                                    x.Value
                            }
                        )
                        .ToList();

                return Ok(new
                {
                    conversationId,

                    type =
                        "servant_roles",

                    count =
                        roles.Count,

                    answer =
                        BuildServantRolesAnswer(),

                    data =
                        roles
                });
            }

            if (functionName ==
                "get_servants_by_role")
            {
                if (!TryGetInt(
                        args,
                        "role_id",
                        out var roleId))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "تعذر تحديد دور الخادم."
                    });
                }

                if (!ServantsRolesStatic.TryGetValue(
                        roleId,
                        out var roleName))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "دور الخادم غير موجود."
                    });
                }

                var servantsGrade =
                    FindServantsGrade(
                        grades
                    );

                if (servantsGrade == null)
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "مجموعة الخدام غير موجودة."
                    });
                }

                var peopleResult =
                    await GetPeopleByGrade(
                        servantsGrade.Id,
                        authorization
                    );

                if (!peopleResult.Success)
                {
                    return ApiError(
                        peopleResult,
                        conversationId
                    );
                }

                var servants =
                    peopleResult
                        .People
                        .Where(
                            x =>
                                x.RoleId ==
                                roleId
                        )
                        .ToList();

                return Ok(new
                {
                    conversationId,

                    type =
                        "servants_by_role",

                    role = new
                    {
                        id =
                            roleId,

                        name =
                            roleName
                    },

                    count =
                        servants.Count,

                    answer =
                        BuildServantsByRoleAnswer(
                            roleName,
                            servants
                        ),

                    data =
                        servants.Select(
                            ToBasicPersonWithRole
                        )
                });
            }

            // =====================================================
            // SEARCH PERSON FIELD
            // =====================================================

            if (functionName ==
                "search_people_by_field")
            {
                if (!TryGetInt(
                        args,
                        "grade_id",
                        out var gradeId))
                {
                    return AskForGrade(
                        new GroupAction
                        {
                            Kind =
                                "people_search"
                        },
                        session,
                        grades,
                        conversationId
                    );
                }

                if (!TryGetString(
                        args,
                        "field",
                        out var field)
                    ||
                    !TryGetString(
                        args,
                        "mode",
                        out var mode))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "تعذر فهم شروط البحث."
                    });
                }

                TryGetString(
                    args,
                    "value",
                    out var value
                );

                value ??= "";

                if (!AllowedSearchFields.Contains(
                    field!))
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "الحقل غير مسموح بالبحث فيه."
                    });
                }

                var grade =
                    grades.FirstOrDefault(
                        x =>
                            x.Id ==
                            gradeId
                    );

                if (grade == null)
                {
                    return BadRequest(new
                    {
                        conversationId,
                        message =
                            "المرحلة غير موجودة."
                    });
                }

                var peopleResult =
                    await GetPeopleByGrade(
                        grade.Id,
                        authorization
                    );

                if (!peopleResult.Success)
                {
                    return ApiError(
                        peopleResult,
                        conversationId
                    );
                }

                var filtered =
                    FilterPeople(
                        peopleResult.People,
                        field!,
                        mode!,
                        value
                    );

                return Ok(new
                {
                    conversationId,

                    type =
                        "people_search",

                    count =
                        filtered.Count,

                    answer =
                        BuildSearchAnswer(
                            grade,
                            field!,
                            filtered
                        ),

                    data =
                        filtered
                });
            }

            return BadRequest(new
            {
                conversationId,

                message =
                    $"الأداة المطلوبة غير مدعومة: {functionName}"
            });
        }

        // =========================================================
        // PERSON ACTION EXECUTION
        // =========================================================

        private async Task<IActionResult>
            ExecutePersonAction(
                PersonAction action,
                ChatSessionState session,
                List<GradeResult> grades,
                string authorization,
                string conversationId)
        {
            var person =
                session.SelectedPerson;

            if (person == null)
            {
                return Ok(new
                {
                    conversationId,

                    type =
                        "person_required",

                    answer =
                        "حدد الشخص المقصود أولاً."
                });
            }

            if (string.IsNullOrWhiteSpace(
                person.Qr))
            {
                return BadRequest(new
                {
                    conversationId,

                    message =
                        "لا يوجد QR صالح لهذا الشخص."
                });
            }

            // =====================================================
            // SUMMARY
            // =====================================================

            if (action.Kind ==
                "summary")
            {
                session.LastPersonAction =
                    ClonePersonAction(
                        action
                    );

                return SelectedPersonSummary(
                    session,
                    conversationId,
                    grades
                );
            }

            // =====================================================
            // DETAILS
            // =====================================================

            if (action.Kind ==
                "details")
            {
                var refreshed =
                    await GetPersonForSelectionByQr(
                        person.Qr,
                        authorization
                    );

                if (refreshed != null)
                {
                    session.SelectedPerson =
                        refreshed;

                    person =
                        refreshed;
                }

                session.LastPersonAction =
                    ClonePersonAction(
                        action
                    );

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        "person_details",

                    answer =
                        BuildPersonDetailsAnswer(
                            person
                        ),

                    data =
                        person,

                    selectedPerson =
                        ToBasicPerson(
                            person
                        ),

                    actions =
                        SelectedPersonActions()
                });
            }

            // =====================================================
            // ATTENDANCE / ABSENCE
            // =====================================================

            if (action.Kind ==
                    "attendance"
                ||
                action.Kind ==
                    "absence")
            {
                var api =
                    await GetExactLocal(
                        $"/api/Attendance/show-attendance/{Uri.EscapeDataString(person.Qr)}",
                        authorization
                    );

                if (!api.Success)
                {
                    return ExactApiResponse(
                        "person_attendance",
                        $"سجل الحضور لـ {person.Name}",
                        api,
                        conversationId,
                        person
                    );
                }

                var records =
                    ParseAttendanceRecords(
                        api.Raw
                    );

                records =
                    FilterAttendanceByPeriod(
                        records,
                        action
                    );

                if (action.Kind ==
                    "attendance")
                {
                    records =
                        records
                            .Where(
                                x =>
                                    x.Status ==
                                    AttendancePresentStatus
                            )
                            .OrderBy(
                                x =>
                                    x.Date
                            )
                            .ToList();
                }
                else
                {
                    records =
                        records
                            .Where(
                                x =>
                                    x.Status ==
                                    AttendanceAbsentStatus
                            )
                            .OrderBy(
                                x =>
                                    x.Date
                            )
                            .ToList();
                }

                session.LastPersonAction =
                    ClonePersonAction(
                        action
                    );

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        action.Kind ==
                        "absence"
                            ? "person_absence"
                            : "person_attendance",

                    person =
                        ToBasicPerson(
                            person
                        ),

                    period =
                        ToPeriodObject(
                            action
                        ),

                    count =
                        records.Count,

                    answer =
                        action.Kind ==
                        "absence"
                            ? BuildAbsenceAnswer(
                                person,
                                records,
                                action
                            )
                            : BuildPresentAnswer(
                                person,
                                records,
                                action
                            ),

                    data =
                        records.Select(
                            ToAttendanceOutput
                        ),

                    actions =
                        SelectedPersonActions()
                });
            }

            // =====================================================
            // PERSON SUBSCRIPTIONS
            // =====================================================

            if (action.Kind ==
                "subscriptions")
            {
                // The legacy person endpoint returns paid rows only.
                // For the normal status view (all/unpaid), use the
                // month endpoint so missing payments are represented
                // correctly instead of being reported as zero.
                if (!HasPeriod(action) &&
                    action.PaymentStatus != "paid")
                {
                    SetCurrentMonth(
                        action,
                        GetEgyptNow()
                    );
                }

                // No period -> use person's direct API.
                if (!HasPeriod(action))
                {
                    var api =
                        await GetExactLocal(
                            $"/api/Subscriptions/show/{Uri.EscapeDataString(person.Qr)}",
                            authorization
                        );

                    if (!api.Success)
                    {
                        return ExactApiResponse(
                            "person_subscriptions",
                            $"اشتراكات {person.Name}",
                            api,
                            conversationId,
                            person
                        );
                    }

                    var records =
                        ParseSubscriptionRecords(
                            api.Raw
                        );

                    session.LastPersonAction =
                        ClonePersonAction(
                            action
                        );

                    TouchSession(session);

                    if (records.Count > 0 ||
                        IsJsonArray(
                            api.Raw
                        ))
                    {
                        var filtered =
                            FilterSubscriptionsByPayment(
                                records,
                                action.PaymentStatus
                            );

                        return Ok(new
                        {
                            conversationId,

                            type =
                                "person_subscriptions",

                            person =
                                ToBasicPerson(
                                    person
                                ),

                            count =
                                filtered.Count,

                            answer =
                                BuildDirectPersonSubscriptionAnswer(
                                    person,
                                    filtered,
                                    action
                                ),

                            data =
                                filtered,

                            actions =
                                SelectedPersonActions()
                        });
                    }

                    return ExactApiResponse(
                        "person_subscriptions",
                        $"اشتراكات {person.Name}",
                        api,
                        conversationId,
                        person
                    );
                }

                var personWithGrade =
                    await EnsurePersonHasGrade(
                        person,
                        authorization
                    );

                if (!personWithGrade.Grade.HasValue)
                {
                    return BadRequest(new
                    {
                        conversationId,

                        message =
                            "تعذر تحديد مرحلة الشخص لقراءة الاشتراكات خلال الفترة."
                    });
                }

                session.SelectedPerson =
                    personWithGrade;

                var monthRange =
                    EnumerateMonths(
                        action.FromDate!.Value,
                        action.ToDate!.Value
                    );

                if (monthRange.Count > 36)
                {
                    return RangeTooLarge(
                        conversationId
                    );
                }

                var periods =
                    new List<SubscriptionMonthResult>();

                foreach (var month in monthRange)
                {
                    var result =
                        await GetSubscriptionsForMonth(
                            personWithGrade.Grade.Value,
                            month.Month,
                            month.Year,
                            authorization
                        );

                    var personRecords =
                        result
                            .Where(
                                x =>
                                    string.Equals(
                                        x.StudentQr,
                                        personWithGrade.Qr,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                            )
                            .ToList();

                    personRecords =
                        FilterSubscriptionsByPayment(
                            personRecords,
                            action.PaymentStatus
                        );

                    periods.Add(
                        new SubscriptionMonthResult
                        {
                            Month =
                                month.Month,

                            Year =
                                month.Year,

                            Records =
                                personRecords
                        }
                    );
                }

                session.LastPersonAction =
                    ClonePersonAction(
                        action
                    );

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        "person_subscriptions_period",

                    person =
                        ToBasicPerson(
                            personWithGrade
                        ),

                    period =
                        ToPeriodObject(
                            action
                        ),

                    paymentStatus =
                        action.PaymentStatus,

                    answer =
                        BuildPersonSubscriptionPeriodAnswer(
                            personWithGrade,
                            periods,
                            action
                        ),

                    data =
                        periods,

                    actions =
                        SelectedPersonActions()
                });
            }

            // =====================================================
            // PERSON VISITATIONS
            // =====================================================

            if (action.Kind ==
                "visitations")
            {
                if (!HasBoundedPeriod(
                        action
                    ))
                {
                    session.PendingPersonAction =
                        ClonePersonAction(
                            action
                        );

                    return AskForPeriod(
                        "person",
                        action.Kind,
                        conversationId
                    );
                }

                var personWithGrade =
                    await EnsurePersonHasGrade(
                        person,
                        authorization
                    );

                if (!personWithGrade.Grade.HasValue)
                {
                    return BadRequest(new
                    {
                        conversationId,

                        message =
                            "تعذر تحديد مرحلة الشخص لقراءة الزيارات."
                    });
                }

                session.SelectedPerson =
                    personWithGrade;

                var visits =
                    await GetVisitationsForRange(
                        personWithGrade.Grade.Value,
                        action.FromDate!.Value,
                        action.ToDate!.Value,
                        authorization
                    );

                visits =
                    visits
                        .Where(
                            x =>
                                string.Equals(
                                    x.StudentQr,
                                    personWithGrade.Qr,
                                    StringComparison.OrdinalIgnoreCase
                                )
                        )
                        .OrderByDescending(
                            x =>
                                x.Date
                        )
                        .ToList();

                session.LastPersonAction =
                    ClonePersonAction(
                        action
                    );

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        "person_visitations",

                    person =
                        ToBasicPerson(
                            personWithGrade
                        ),

                    period =
                        ToPeriodObject(
                            action
                        ),

                    count =
                        visits.Count,

                    answer =
                        BuildPersonVisitationAnswer(
                            personWithGrade,
                            visits,
                            action
                        ),

                    data =
                        visits,

                    actions =
                        SelectedPersonActions()
                });
            }

            return BadRequest(new
            {
                conversationId,

                message =
                    "نوع الطلب غير معروف."
            });
        }

        // =========================================================
        // GROUP ACTION EXECUTION
        // =========================================================

        private async Task<IActionResult>
            ExecuteGroupAction(
                GroupAction action,
                ChatSessionState session,
                List<GradeResult> grades,
                string authorization,
                string conversationId)
        {
            if (!action.GradeId.HasValue)
            {
                return AskForGrade(
                    action,
                    session,
                    grades,
                    conversationId
                );
            }

            var grade =
                grades.FirstOrDefault(
                    x =>
                        x.Id ==
                        action.GradeId.Value
                );

            if (grade == null)
            {
                return BadRequest(new
                {
                    conversationId,

                    message =
                        "المرحلة غير موجودة حالياً."
                });
            }

            session.LastGradeId =
                grade.Id;

            // =====================================================
            // PEOPLE LIST
            // =====================================================

            if (action.Kind ==
                "people")
            {
                var peopleResult =
                    await GetPeopleByGrade(
                        grade.Id,
                        authorization
                    );

                if (!peopleResult.Success)
                {
                    return ApiError(
                        peopleResult,
                        conversationId
                    );
                }

                return Ok(new
                {
                    conversationId,

                    type =
                        "people_list",

                    grade = new
                    {
                        id =
                            grade.Id,

                        name =
                            grade.Name
                    },

                    count =
                        peopleResult.People.Count,

                    answer =
                        BuildPeopleAnswer(
                            grade,
                            peopleResult.People
                        ),

                    data =
                        peopleResult.People
                            .Select(
                                ToBasicPerson
                            )
                });
            }

            // =====================================================
            // GROUP QUERIES REQUIRE PERIOD
            //
            // Except attendance/absence when ALL TIME explicitly
            // =====================================================

            if (!HasPeriod(action))
            {
                session.PendingGroupAction =
                    CloneGroupAction(
                        action
                    );

                return AskForPeriod(
                    "group",
                    action.Kind,
                    conversationId
                );
            }

            if (action.AllTime &&
                (
                    action.Kind ==
                    "subscriptions"
                    ||
                    action.Kind ==
                    "visitations"
                ))
            {
                session.PendingGroupAction =
                    CloneGroupAction(
                        action
                    );

                session.PendingGroupAction.AllTime =
                    false;

                return Ok(new
                {
                    conversationId,

                    type =
                        "period_required",

                    answer =
                        "حدد بداية ونهاية الفترة المطلوبة، مثال: من 1/1/2026 لحد دلوقتي."
                });
            }

            // =====================================================
            // ATTENDANCE / ABSENCE BY GROUP
            // =====================================================

            if (action.Kind ==
                    "attendance"
                ||
                action.Kind ==
                    "absence")
            {
                var peopleResult =
                    await GetPeopleByGrade(
                        grade.Id,
                        authorization
                    );

                if (!peopleResult.Success)
                {
                    return ApiError(
                        peopleResult,
                        conversationId
                    );
                }

                var aggregates =
                    await GetAttendanceForPeople(
                        peopleResult.People,
                        action,
                        authorization
                    );

                var wantedStatus =
                    action.Kind ==
                    "absence"
                        ? AttendanceAbsentStatus
                        : AttendancePresentStatus;

                var matches =
                    aggregates
                        .Select(
                            x =>
                            {
                                x.Records =
                                    x.Records
                                        .Where(
                                            r =>
                                                r.Status ==
                                                wantedStatus
                                        )
                                        .OrderBy(
                                            r =>
                                                r.Date
                                        )
                                        .ToList();

                                return x;
                            }
                        )
                        .Where(
                            x =>
                                x.Records.Count > 0
                        )
                        .OrderByDescending(
                            x =>
                                x.Records.Count
                        )
                        .ThenBy(
                            x =>
                                x.Person.Name
                        )
                        .ToList();

                session.LastGroupAction =
                    CloneGroupAction(
                        action
                    );

                session.PendingGroupAction =
                    null;

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        action.Kind ==
                        "absence"
                            ? "group_absence"
                            : "group_attendance",

                    grade = new
                    {
                        id =
                            grade.Id,

                        name =
                            grade.Name
                    },

                    period =
                        ToPeriodObject(
                            action
                        ),

                    peopleCount =
                        matches.Count,

                    recordsCount =
                        matches.Sum(
                            x =>
                                x.Records.Count
                        ),

                    answer =
                        BuildGroupAttendanceAnswer(
                            grade,
                            matches,
                            action
                        ),

                    data =
                        matches.Select(
                            x => new
                            {
                                person =
                                    ToBasicPerson(
                                        x.Person
                                    ),

                                count =
                                    x.Records.Count,

                                records =
                                    x.Records.Select(
                                        ToAttendanceOutput
                                    )
                            }
                        )
                });
            }

            // =====================================================
            // GROUP SUBSCRIPTIONS
            // =====================================================

            if (action.Kind ==
                "subscriptions")
            {
                if (!HasBoundedPeriod(
                        action
                    ))
                {
                    session.PendingGroupAction =
                        CloneGroupAction(
                            action
                        );

                    return AskForPeriod(
                        "group",
                        action.Kind,
                        conversationId
                    );
                }

                return await ExecuteGroupSubscriptionBalanceReport(
                    grade,
                    action,
                    session,
                    authorization,
                    conversationId
                );
            }

            // =====================================================
            // GROUP VISITATIONS
            // =====================================================

            if (action.Kind ==
                "visitations")
            {
                if (!HasBoundedPeriod(
                        action
                    ))
                {
                    session.PendingGroupAction =
                        CloneGroupAction(
                            action
                        );

                    return AskForPeriod(
                        "group",
                        action.Kind,
                        conversationId
                    );
                }

                var visits =
                    await GetVisitationsForRange(
                        grade.Id,
                        action.FromDate!.Value,
                        action.ToDate!.Value,
                        authorization
                    );

                session.LastGroupAction =
                    CloneGroupAction(
                        action
                    );

                session.PendingGroupAction =
                    null;

                TouchSession(session);

                return Ok(new
                {
                    conversationId,

                    type =
                        "group_visitations",

                    grade = new
                    {
                        id =
                            grade.Id,

                        name =
                            grade.Name
                    },

                    period =
                        ToPeriodObject(
                            action
                        ),

                    visitsCount =
                        visits.Count,

                    peopleCount =
                        visits
                            .Where(
                                x =>
                                    !string.IsNullOrWhiteSpace(
                                        x.StudentQr
                                    )
                            )
                            .Select(
                                x =>
                                    x.StudentQr
                            )
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase
                            )
                            .Count(),

                    answer =
                        BuildGroupVisitationAnswer(
                            grade,
                            visits,
                            action
                        ),

                    data =
                        visits
                });
            }

            return BadRequest(new
            {
                conversationId,

                message =
                    "نوع طلب المجموعة غير معروف."
            });
        }

        // =========================================================
        // ASK USER FOR GRADE
        // =========================================================

        private IActionResult AskForGrade(
            GroupAction action,
            ChatSessionState session,
            List<GradeResult> grades,
            string conversationId)
        {
            session.PendingGroupAction =
                CloneGroupAction(
                    action
                );

            TouchSession(session);

            return Ok(new
            {
                conversationId,

                type =
                    "grade_required",

                answer =
                    "اكتب اسم المرحلة المطلوبة علشان أجيب النتيجة بدقة.",

                availableGrades =
                    grades.Select(
                        x => new
                        {
                            id = x.Id,
                            name = x.Name
                        }
                    ),

                pending = new
                {
                    kind =
                        action.Kind,

                    fromDate =
                        action.FromDate?.ToString(
                            "yyyy-MM-dd"
                        ),

                    toDate =
                        action.ToDate?.ToString(
                            "yyyy-MM-dd"
                        ),

                    allTime =
                        action.AllTime,

                    includeAudit =
                        action.IncludeAudit,

                    paymentStatus =
                        action.PaymentStatus
                }
            });
        }

        // =========================================================
        // ASK USER FOR PERIOD
        // =========================================================

        private IActionResult AskForPeriod(
            string scope,
            string kind,
            string conversationId)
        {
            return Ok(new
            {
                conversationId,

                type =
                    "period_required",

                answer =
                    "اكتب الفترة المطلوبة، مثل: الشهر ده أو الشهر اللي فات أو من 1/1/2026 لحد دلوقتي.",

                pending = new
                {
                    scope,
                    kind
                }
            });
        }

        // =========================================================
        // PERSON RESOLUTION
        // =========================================================

        private async Task<IActionResult>
            ResolvePersonAndExecute(
                string name,
                PersonAction action,
                ChatSessionState session,
                List<GradeResult> grades,
                string authorization,
                string conversationId)
        {
            var matches =
                await FindPeopleByName(
                    name,
                    grades,
                    authorization
                );

            if (matches.Count == 0)
            {
                return Ok(new
                {
                    conversationId,

                    type =
                        "person_not_found",

                    answer =
                        $"لم أجد شخصاً باسم \"{name}\" ضمن البيانات المسموح لك بعرضها."
                });
            }

            if (matches.Count > 1)
            {
                return PersonAmbiguousResponse(
                    name,
                    matches,
                    grades,
                    action,
                    session,
                    conversationId
                );
            }

            SetSelectedPerson(
                session,
                matches[0]
            );

            return await ExecutePersonAction(
                action,
                session,
                grades,
                authorization,
                conversationId
            );
        }

        private IActionResult PersonAmbiguousResponse(
            string requestedName,
            List<PersonResult> matches,
            List<GradeResult> grades,
            PersonAction action,
            ChatSessionState session,
            string conversationId)
        {
            session.PendingCandidates =
                matches;

            // هنا أهم إصلاح:
            // نحفظ الطلب الأصلي بالكامل
            // مش مجرد summary.
            session.PendingPersonAction =
                ClonePersonAction(
                    action
                );

            TouchSession(session);

            var options =
                matches
                    .Select(
                        (person, index) =>
                        {
                            var gradeName =
                                grades
                                    .FirstOrDefault(
                                        x =>
                                            x.Id ==
                                            person.Grade
                                    )
                                    ?.Name
                                ??
                                "غير محدد";

                            return new
                            {
                                action =
                                    "select_person",

                                label =
                                    $"{person.Name} — {gradeName}",

                                selectedQr =
                                    person.Qr,

                                index =
                                    index + 1
                            };
                        }
                    )
                    .ToList();

            var answer =
                new StringBuilder();

            answer.AppendLine(
                $"وجدت أكثر من شخص مطابق للاسم \"{requestedName}\"."
            );

            answer.AppendLine(
                "اختار الشخص المقصود:"
            );

            answer.AppendLine();

            for (var i = 0; i < options.Count; i++)
            {
                answer.AppendLine(
                    $"{i + 1}. {options[i].label}"
                );
            }

            return Ok(new
            {
                conversationId,

                type =
                    "person_ambiguous",

                answer =
                    answer.ToString(),

                ui = new
                {
                    type =
                        "choice_buttons",

                    options
                },

                pending = new
                {
                    action =
                        action.Kind,

                    fromDate =
                        action.FromDate?.ToString(
                            "yyyy-MM-dd"
                        ),

                    toDate =
                        action.ToDate?.ToString(
                            "yyyy-MM-dd"
                        ),

                    allTime =
                        action.AllTime,

                    includeAudit =
                        action.IncludeAudit,

                    paymentStatus =
                        action.PaymentStatus
                }
            });
        }

        // =========================================================
        // BUILD PERSON ACTION FROM TOOL
        // =========================================================

        private static PersonAction BuildPersonActionFromTool(
            string functionName,
            JsonElement args,
            string message,
            DateTime now,
            ChatSessionState session)
        {
            var action =
                new PersonAction();

            action.Kind =
                functionName switch
                {
                    "get_person_details_by_name" =>
                        "details",

                    "get_selected_person_details" =>
                        "details",

                    "get_person_attendance_by_name" =>
                        "attendance",

                    "get_selected_person_attendance" =>
                        "attendance",

                    "get_person_absence_by_name" =>
                        "absence",

                    "get_selected_person_absence" =>
                        "absence",

                    "get_person_subscriptions_by_name" =>
                        "subscriptions",

                    "get_selected_person_subscriptions" =>
                        "subscriptions",

                    "get_person_visitations_by_name" =>
                        "visitations",

                    "get_selected_person_visitations" =>
                        "visitations",

                    "get_selected_person_summary" =>
                        "summary",

                    _ =>
                        "summary"
                };

            TryGetBool(
                args,
                "include_audit",
                out var includeAudit
            );

            action.IncludeAudit =
                includeAudit
                ||
                ContainsAuditIntent(
                    NormalizeArabic(message)
                );

            TryGetString(
                args,
                "payment_status",
                out var paymentStatus
            );

            action.PaymentStatus =
                ResolvePaymentStatus(
                    paymentStatus,
                    message
                );

            ApplyToolDates(
                args,
                action
            );

            if (TryResolveDateRangeFromMessage(
                    message,
                    now,
                    out var fromDate,
                    out var toDate,
                    out var allTime))
            {
                action.FromDate =
                    fromDate;

                action.ToDate =
                    toDate;

                action.AllTime =
                    allTime;
            }

            return action;
        }

        // =========================================================
        // BUILD GROUP ACTION FROM TOOL
        // =========================================================

        private static GroupAction BuildGroupActionFromTool(
            string functionName,
            JsonElement args,
            string message,
            DateTime now)
        {
            var action =
                new GroupAction
                {
                    Kind =
                        functionName switch
                        {
                            "get_group_attendance" =>
                                "attendance",

                            "get_group_absence" =>
                                "absence",

                            "get_group_subscriptions" =>
                                "subscriptions",

                            "get_group_visitations" =>
                                "visitations",

                            _ =>
                                ""
                        }
                };

            if (TryGetInt(
                    args,
                    "grade_id",
                    out var gradeId))
            {
                action.GradeId =
                    gradeId;
            }

            TryGetBool(
                args,
                "include_audit",
                out var includeAudit
            );

            action.IncludeAudit =
                includeAudit
                ||
                ContainsAuditIntent(
                    NormalizeArabic(message)
                );

            TryGetString(
                args,
                "payment_status",
                out var paymentStatus
            );

            action.PaymentStatus =
                ResolvePaymentStatus(
                    paymentStatus,
                    message
                );

            ApplyToolDates(
                args,
                action
            );

            if (TryResolveDateRangeFromMessage(
                    message,
                    now,
                    out var fromDate,
                    out var toDate,
                    out var allTime))
            {
                action.FromDate =
                    fromDate;

                action.ToDate =
                    toDate;

                action.AllTime =
                    allTime;
            }

            if (!HasPeriod(action) &&
                action.Kind is
                    "attendance" or
                    "absence" or
                    "subscriptions" or
                    "visitations")
            {
                SetCurrentMonth(
                    action,
                    now
                );
            }

            return action;
        }

        // =========================================================
        // BACKEND INTENT SAFETY NET
        // =========================================================

        private static bool TryBuildDeterministicGroupAction(
            string message,
            List<GradeResult> grades,
            DateTime now,
            out GroupAction action)
        {
            action = new GroupAction();

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var normalized = NormalizeArabic(message);

            var isAbsence = ContainsAny(
                normalized,
                "غياب",
                "غيابات",
                "غابوا",
                "غايبين",
                "متغيبين",
                "مش بيحضروا",
                "مبيحضروش"
            );

            var isAttendance = ContainsAny(
                normalized,
                "حضور",
                "حاضرين",
                "بيحضروا",
                "حضروا"
            );

            var isSubscriptions = ContainsAny(
                normalized,
                "اشتراك",
                "اشتراكات",
                "الاشتراك",
                "الاشتراكات",
                "مدفوع",
                "مدفوعين",
                "غير مدفوع",
                "مدفعوش"
            );

            var isVisitations = ContainsAny(
                normalized,
                "زياره",
                "زيارة",
                "زيارات"
            );

            if (!isAbsence &&
                !isAttendance &&
                !isSubscriptions &&
                !isVisitations)
            {
                return false;
            }

            var hasGrade =
                TryResolveGradeFromMessage(
                    message,
                    grades,
                    out var gradeId
                );

            var mentionsGrade = ContainsAny(
                normalized,
                "مرحله",
                "المرحله",
                "صف",
                "ابتدائي",
                "ابتدايي",
                "اعدادي",
                "اعداديه",
                "ثانوي",
                "ثانويه",
                "جامعه",
                "خريجين",
                "خدام",
                "خادم",
                "خدامين"
            );

            var mentionsGroup = ContainsAny(
                normalized,
                "طلاب",
                "الطلاب",
                "الناس",
                "المجموعه",
                "المجموعة",
                "الجروب",
                "الكل",
                "اللي مدفعوش",
                "اللي دفعوا",
                "اللي غابوا",
                "اللي حضروا"
            );

            // A request containing a person's name should continue to
            // the person resolver.  Only route a request without a
            // grade when it is clearly a bare group request.
            var isBareGroupRequest =
                IsBareGroupDataRequest(normalized);

            if (!hasGrade && !mentionsGrade && !mentionsGroup &&
                !isBareGroupRequest)
            {
                return false;
            }

            action.Kind = isAbsence
                ? "absence"
                : isAttendance
                    ? "attendance"
                    : isSubscriptions
                        ? "subscriptions"
                        : "visitations";

            action.GradeId = hasGrade ? gradeId : null;
            action.PaymentStatus = DetectPaymentStatus(message);
            action.IncludeAudit = ContainsAuditIntent(normalized);

            if (TryResolveDateRangeFromMessage(
                    message,
                    now,
                    out var fromDate,
                    out var toDate,
                    out var allTime))
            {
                action.FromDate = fromDate;
                action.ToDate = toDate;
                action.AllTime = allTime;
            }
            else
            {
                // A group query without a period means the current
                // month.  This keeps the response useful and avoids
                // an unnecessary period quick-reply step.
                SetCurrentMonth(action, now);
            }

            return true;
        }

        private static bool IsBareGroupDataRequest(string normalized)
        {
            var value = Regex.Replace(
                normalized,
                @"\d{1,4}[/-]\d{1,2}[/-]\d{1,4}|\d+",
                " "
            );

            var removableWords = new HashSet<string>(StringComparer.Ordinal)
            {
                "هات", "اعرض", "جيب", "عايز", "عاوزه", "عاوز", "ممكن", "شوف",
                "وريني", "بيانات", "كل", "الشهر", "شهر", "ده", "دا", "دي",
                "الحالي", "الحاليه", "اللي", "من", "في", "ف", "ل", "غياب",
                "الغياب", "غيابه", "غيابات", "غابوا", "غايبين", "حضور", "حاضرين",
                "بيحضروا", "حضروا", "اشتراك", "الاشتراك", "اشتراكات", "الاشتراكات",
                "مدفوع", "مدفوعين", "غير", "مدفعوش", "زياره", "الزياره", "زيارات",
                "فات", "الماضي", "الماضيه", "اخر", "دلوقتي", "الان", "حتى", "لحد"
            };

            var remaining = value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim('،', ',', '.', '؟', '?', ':', ';', 'ـ'))
                .Where(token => token.Length > 0 && !removableWords.Contains(token));

            return !remaining.Any();
        }

        private static bool TryExtractPersonRequest(
            string message,
            DateTime now,
            ChatSessionState session,
            out string personQuery,
            out PersonAction action)
        {
            personQuery = "";
            action = new PersonAction();

            var inferred = InferPersonActionFromMessage(
                message,
                now,
                session
            );

            if (inferred == null ||
                inferred.Kind is not (
                    "attendance" or "absence" or "subscriptions" or "visitations"))
            {
                return false;
            }

            var residual = NormalizeArabic(
                ConvertArabicDigits(message)
            );

            residual = Regex.Replace(
                residual,
                @"\d{1,4}[/-]\d{1,2}[/-]\d{1,4}|\d+",
                " "
            );

            residual = Regex.Replace(
                residual,
                @"(?<![\p{L}\p{N}])شهر\s+\d{1,2}(?![\p{L}\p{N}])",
                " "
            );

            var removableWords = new HashSet<string>(StringComparer.Ordinal)
            {
                "هات", "اعرض", "جيب", "شوف", "وريني", "عايز", "عاوزه", "عاوز",
                "ممكن", "لو", "سمحت", "بيانات", "تفاصيل", "الغياب", "غياب",
                "غيابه", "غيابها", "غيابات", "غاب", "غايب", "حضور", "حضوره",
                "اشتراك", "الاشتراك", "اشتراكات", "الاشتراكات", "اشتراكاته",
                "زياره", "الزياره", "زيارات", "زياراته", "الشهر", "شهر", "ده",
                "دا", "دي", "هذا", "الحالي", "الحاليه", "اللي", "فات", "فاته",
                "الماضي", "الماضيه", "اخر", "من", "لحد", "حتى", "دلوقتي", "الان",
                "مرحله", "المرحله", "صف", "الصف", "انهي", "اي", "ايه", "فانهي",
                "هو", "هي", "ده", "دا", "معرفش", "مش", "المطلوب", "طب", "طيب",
                "في", "ف", "ل", "كل", "افراد", "طلاب", "الطلاب", "الناس", "المجموعه"
            };

            var tokens = residual
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim('،', ',', '.', '؟', '?', ':', ';', 'ـ'))
                .Where(token => token.Length > 0 && !removableWords.Contains(token))
                .ToList();

            if (tokens.Count == 0)
            {
                return false;
            }

            personQuery = string.Join(' ', tokens);
            action = inferred;
            return true;
        }

        private static bool HasExplicitGroupScope(
            string message,
            List<GradeResult> grades)
        {
            var normalized = NormalizeArabic(message);

            return TryResolveGradeFromMessage(
                       message,
                       grades,
                       out _
                   )
                   ||
                   ContainsAny(
                       normalized,
                       "طلاب",
                       "الطلاب",
                       "الناس",
                       "المجموعه",
                       "المجموعة",
                       "الجروب",
                       "الكل",
                       "اللي مدفعوش",
                       "اللي دفعوا",
                       "اللي غابوا",
                       "اللي حضروا"
                   );
        }

        private static void SetCurrentMonth(
            GroupAction action,
            DateTime now)
        {
            action.AllTime = false;
            action.FromDate = new DateTime(now.Year, now.Month, 1);
            action.ToDate = now.Date;
        }

        private static void SetCurrentMonth(
            PersonAction action,
            DateTime now)
        {
            action.AllTime = false;
            action.FromDate = new DateTime(now.Year, now.Month, 1);
            action.ToDate = now.Date;
        }

        private static DateTime GetStudentCalculationStart(
            PersonResult person,
            DateTime configuredStart)
        {
            if (!DateTime.TryParse(
                    person.CreatedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var createdAt))
            {
                return configuredStart;
            }

            var studentStart = new DateTime(
                createdAt.Year,
                createdAt.Month,
                1
            );

            return studentStart > configuredStart
                ? studentStart
                : configuredStart;
        }

        private static PersonAction?
            InferPersonActionFromMessage(
                string message,
                DateTime now,
                ChatSessionState session)
        {
            if (string.IsNullOrWhiteSpace(
                message))
            {
                return null;
            }

            var normalized =
                NormalizeArabic(
                    message
                );

            var includeAudit =
                ContainsAuditIntent(
                    normalized
                );

            string? kind =
                null;

            if (ContainsAny(
                    normalized,
                    "غياب",
                    "غيابه",
                    "غيابات",
                    "غاب",
                    "غايب",
                    "غائب"))
            {
                kind =
                    "absence";
            }
            else if (ContainsAny(
                         normalized,
                         "حضور",
                         "حضوره",
                         "حضر"))
            {
                kind =
                    "attendance";
            }
            else if (ContainsAny(
                         normalized,
                         "اشتراك",
                         "اشتراكاته",
                         "اشتراكات",
                         "دفع الاشتراك"))
            {
                kind =
                    "subscriptions";
            }
            else if (ContainsAny(
                         normalized,
                         "زياره",
                         "زيارة",
                         "زيارات",
                         "زار",
                         "اتزار"))
            {
                kind =
                    "visitations";
            }
            else if (ContainsAny(
                         normalized,
                         "بياناته",
                         "تفاصيله",
                         "رقمه",
                         "تليفونه",
                         "تلفونه",
                         "عنوانه",
                         "اب اعتراف",
                         "أب اعتراف",
                         "وظيفته",
                         "دوره",
                         "ملاحظاته"))
            {
                kind =
                    "details";
            }

            // Follow up:
            // "طب الشهر ده"
            if (kind == null &&
                session.LastPersonAction != null)
            {
                if (TryResolveDateRangeFromMessage(
                        message,
                        now,
                        out var nextFrom,
                        out var nextTo,
                        out var nextAll))
                {
                    var follow =
                        ClonePersonAction(
                            session.LastPersonAction
                        );

                    follow.FromDate =
                        nextFrom;

                    follow.ToDate =
                        nextTo;

                    follow.AllTime =
                        nextAll;

                    follow.IncludeAudit =
                        includeAudit
                        ||
                        follow.IncludeAudit;

                    return follow;
                }
            }

            // Follow up:
            // "مين سجل الغياب ده؟"
            if (kind == null &&
                includeAudit &&
                session.LastPersonAction != null)
            {
                var follow =
                    ClonePersonAction(
                        session.LastPersonAction
                    );

                follow.IncludeAudit =
                    true;

                return follow;
            }

            if (kind == null)
            {
                return null;
            }

            var action =
                new PersonAction
                {
                    Kind =
                        kind,

                    IncludeAudit =
                        includeAudit,

                    PaymentStatus =
                        DetectPaymentStatus(
                            message
                        )
                };

            if (TryResolveDateRangeFromMessage(
                    message,
                    now,
                    out var fromDate,
                    out var toDate,
                    out var allTime))
            {
                action.FromDate =
                    fromDate;

                action.ToDate =
                    toDate;

                action.AllTime =
                    allTime;
            }

            return action;
        }

        // =========================================================
        // PERIOD PARSER
        // =========================================================

        private static bool TryResolveDateRangeFromMessage(
            string message,
            DateTime now,
            out DateTime? fromDate,
            out DateTime? toDate,
            out bool allTime)
        {
            fromDate =
                null;

            toDate =
                null;

            allTime =
                false;

            var converted =
                ConvertArabicDigits(
                    message
                );

            var normalized =
                NormalizeArabic(
                    converted
                );

            // =====================================================
            // ALL TIME
            // =====================================================

            if (ContainsAny(
                    normalized,
                    "كل المده",
                    "كل المدة",
                    "كل الفتره",
                    "كل الفترة",
                    "من البدايه",
                    "من البداية",
                    "من اول ما",
                    "من اول السجلات"))
            {
                allTime =
                    true;

                return true;
            }

            // =====================================================
            // CURRENT MONTH
            // =====================================================

            if (ContainsAny(
                    normalized,
                    "الشهر ده",
                    "الشهر دا",
                    "الشهر الحالي",
                    "هذا الشهر"))
            {
                fromDate =
                    new DateTime(
                        now.Year,
                        now.Month,
                        1
                    );

                toDate =
                    now;

                return true;
            }

            // =====================================================
            // PREVIOUS MONTH
            // =====================================================

            if (ContainsAny(
                    normalized,
                    "الشهر اللي فات",
                    "الشهر ال فات",
                    "الشهر الفات",
                    "الشهر الماضي",
                    "الشهر السابق",
                    "الشهر لي فات"))
            {
                var previous =
                    now.AddMonths(-1);

                fromDate =
                    new DateTime(
                        previous.Year,
                        previous.Month,
                        1
                    );

                toDate =
                    fromDate
                        .Value
                        .AddMonths(1)
                        .AddDays(-1);

                return true;
            }

            // =====================================================
            // CURRENT YEAR
            // =====================================================

            if (ContainsAny(
                    normalized,
                    "السنه دي",
                    "السنة دي",
                    "السنه الحاليه",
                    "السنة الحالية"))
            {
                fromDate =
                    new DateTime(
                        now.Year,
                        1,
                        1
                    );

                toDate =
                    now;

                return true;
            }

            // =====================================================
            // PREVIOUS YEAR
            // =====================================================

            if (ContainsAny(
                    normalized,
                    "السنه اللي فاتت",
                    "السنة اللي فاتت",
                    "السنه الماضيه",
                    "السنة الماضية"))
            {
                fromDate =
                    new DateTime(
                        now.Year - 1,
                        1,
                        1
                    );

                toDate =
                    new DateTime(
                        now.Year - 1,
                        12,
                        31
                    );

                return true;
            }

            // =====================================================
            // LAST N DAYS
            // =====================================================

            var daysMatch =
                Regex.Match(
                    normalized,
                    @"(?:اخر|آخر)\s+(\d+)\s+يوم"
                );

            if (daysMatch.Success &&
                int.TryParse(
                    daysMatch.Groups[1].Value,
                    out var days)
                &&
                days > 0)
            {
                fromDate =
                    now.AddDays(
                        -(days - 1)
                    );

                toDate =
                    now;

                return true;
            }

            // =====================================================
            // LAST N MONTHS
            // =====================================================

            var monthsMatch =
                Regex.Match(
                    normalized,
                    @"(?:اخر|آخر)\s+(\d+)\s+(?:شهر|شهور)"
                );

            if (monthsMatch.Success &&
                int.TryParse(
                    monthsMatch.Groups[1].Value,
                    out var months)
                &&
                months > 0)
            {
                var start =
                    now.AddMonths(
                        -(months - 1)
                    );

                fromDate =
                    new DateTime(
                        start.Year,
                        start.Month,
                        1
                    );

                toDate =
                    now;

                return true;
            }

            // =====================================================
            // EXPLICIT DATES
            // =====================================================

            var dateMatches =
                Regex.Matches(
                    converted,
                    @"\b\d{1,4}[\/\-]\d{1,2}[\/\-]\d{1,4}\b"
                );

            var parsedDates =
                new List<DateTime>();

            foreach (Match match in dateMatches)
            {
                if (TryParseFlexibleDate(
                        match.Value,
                        out var parsed))
                {
                    parsedDates.Add(
                        parsed.Date
                    );
                }
            }

            if (parsedDates.Count >= 2)
            {
                fromDate =
                    parsedDates[0];

                toDate =
                    parsedDates[1];

                if (fromDate > toDate)
                {
                    (
                        fromDate,
                        toDate
                    )
                    =
                    (
                        toDate,
                        fromDate
                    );
                }

                return true;
            }

            if (parsedDates.Count == 1)
            {
                if (ContainsAny(
                        normalized,
                        "لحد دلوقتي",
                        "حتى دلوقتي",
                        "لحد الان",
                        "حتى الان",
                        "لحد النهارده",
                        "حتى اليوم"))
                {
                    fromDate =
                        parsedDates[0];

                    toDate =
                        now;

                    return true;
                }

                if (normalized.Contains(
                    "من"))
                {
                    // Open range:
                    // من 1/9/2026
                    fromDate =
                        parsedDates[0];

                    toDate =
                        now;

                    return true;
                }

                if (ContainsAny(
                        normalized,
                        "يوم",
                        "بتاريخ",
                        "تاريخ"))
                {
                    fromDate =
                        parsedDates[0];

                    toDate =
                        parsedDates[0];

                    return true;
                }
            }

            // =====================================================
            // ARABIC MONTH NAME
            // =====================================================

            var monthNames =
                new Dictionary<string, int>
                {
                    { "يناير", 1 },
                    { "فبراير", 2 },
                    { "مارس", 3 },
                    { "ابريل", 4 },
                    { "مايو", 5 },
                    { "يونيو", 6 },
                    { "يوليو", 7 },
                    { "اغسطس", 8 },
                    { "سبتمبر", 9 },
                    { "اكتوبر", 10 },
                    { "نوفمبر", 11 },
                    { "ديسمبر", 12 }
                };

            foreach (var monthName in monthNames)
            {
                if (!normalized.Contains(
                    NormalizeArabic(
                        monthName.Key
                    )))
                {
                    continue;
                }

                var year =
                    ExtractYearFromMessage(
                        normalized
                    )
                    ??
                    now.Year;

                if (!ExtractYearFromMessage(
                        normalized
                    ).HasValue
                    &&
                    ContainsAny(
                        normalized,
                        "لحد دلوقتي",
                        "حتى دلوقتي",
                        "لحد الان",
                        "حتى الان")
                    &&
                    monthName.Value >
                    now.Month)
                {
                    year =
                        now.Year - 1;
                }

                fromDate =
                    new DateTime(
                        year,
                        monthName.Value,
                        1
                    );

                if (ContainsAny(
                        normalized,
                        "لحد دلوقتي",
                        "حتى دلوقتي",
                        "لحد الان",
                        "حتى الان"))
                {
                    toDate =
                        now;
                }
                else
                {
                    toDate =
                        fromDate
                            .Value
                            .AddMonths(1)
                            .AddDays(-1);
                }

                return true;
            }

            // =====================================================
            // "شهر 9" / "شهر 9/2026"
            // =====================================================

            var numericMonth =
                Regex.Match(
                    normalized,
                    @"شهر\s+(\d{1,2})(?:\s*[\/\-]\s*(\d{4}))?"
                );

            if (numericMonth.Success &&
                int.TryParse(
                    numericMonth.Groups[1].Value,
                    out var monthNumber)
                &&
                monthNumber >= 1 &&
                monthNumber <= 12)
            {
                var year =
                    now.Year;

                if (numericMonth.Groups[2].Success &&
                    int.TryParse(
                        numericMonth.Groups[2].Value,
                        out var parsedYear))
                {
                    year =
                        parsedYear;
                }

                fromDate =
                    new DateTime(
                        year,
                        monthNumber,
                        1
                    );

                if (ContainsAny(
                        normalized,
                        "لحد دلوقتي",
                        "حتى دلوقتي",
                        "لحد الان",
                        "حتى الان"))
                {
                    if (!numericMonth.Groups[2].Success &&
                        monthNumber >
                        now.Month)
                    {
                        fromDate =
                            new DateTime(
                                now.Year - 1,
                                monthNumber,
                                1
                            );
                    }

                    toDate =
                        now;
                }
                else
                {
                    toDate =
                        fromDate
                            .Value
                            .AddMonths(1)
                            .AddDays(-1);
                }

                return true;
            }

            return false;
        }

        // =========================================================
        // TOOL DATE PARSING
        // =========================================================

        private static void ApplyToolDates(
            JsonElement args,
            PersonAction action)
        {
            if (TryGetBool(
                    args,
                    "all_time",
                    out var allTime)
                &&
                allTime)
            {
                action.AllTime =
                    true;

                action.FromDate =
                    null;

                action.ToDate =
                    null;

                return;
            }

            if (TryGetString(
                    args,
                    "from_date",
                    out var from)
                &&
                TryParseFlexibleDate(
                    from!,
                    out var fromDate))
            {
                action.FromDate =
                    fromDate.Date;
            }

            if (TryGetString(
                    args,
                    "to_date",
                    out var to)
                &&
                TryParseFlexibleDate(
                    to!,
                    out var toDate))
            {
                action.ToDate =
                    toDate.Date;
            }

            NormalizeRange(
                action
            );
        }

        private static void ApplyToolDates(
            JsonElement args,
            GroupAction action)
        {
            if (TryGetBool(
                    args,
                    "all_time",
                    out var allTime)
                &&
                allTime)
            {
                action.AllTime =
                    true;

                action.FromDate =
                    null;

                action.ToDate =
                    null;

                return;
            }

            if (TryGetString(
                    args,
                    "from_date",
                    out var from)
                &&
                TryParseFlexibleDate(
                    from!,
                    out var fromDate))
            {
                action.FromDate =
                    fromDate.Date;
            }

            if (TryGetString(
                    args,
                    "to_date",
                    out var to)
                &&
                TryParseFlexibleDate(
                    to!,
                    out var toDate))
            {
                action.ToDate =
                    toDate.Date;
            }

            NormalizeRange(
                action
            );
        }

        private static void NormalizeRange(
            PersonAction action)
        {
            if (action.AllTime)
            {
                return;
            }

            if (action.FromDate.HasValue &&
                !action.ToDate.HasValue)
            {
                action.ToDate =
                    GetEgyptNow().Date;
            }

            if (action.FromDate.HasValue &&
                action.ToDate.HasValue &&
                action.FromDate >
                action.ToDate)
            {
                (
                    action.FromDate,
                    action.ToDate
                )
                =
                (
                    action.ToDate,
                    action.FromDate
                );
            }
        }

        private static void NormalizeRange(
            GroupAction action)
        {
            if (action.AllTime)
            {
                return;
            }

            if (action.FromDate.HasValue &&
                !action.ToDate.HasValue)
            {
                action.ToDate =
                    GetEgyptNow().Date;
            }

            if (action.FromDate.HasValue &&
                action.ToDate.HasValue &&
                action.FromDate >
                action.ToDate)
            {
                (
                    action.FromDate,
                    action.ToDate
                )
                =
                (
                    action.ToDate,
                    action.FromDate
                );
            }
        }

        // =========================================================
        // ATTENDANCE FOR WHOLE GRADE
        // =========================================================

        private async Task<List<PersonAttendanceAggregate>>
            GetAttendanceForPeople(
                List<PersonResult> people,
                GroupAction action,
                string authorization)
        {
            var semaphore =
                new SemaphoreSlim(
                    8
                );

            var tasks =
                people
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(
                                x.Qr
                            )
                    )
                    .Select(
                        async person =>
                        {
                            await semaphore.WaitAsync();

                            try
                            {
                                var api =
                                    await GetExactLocal(
                                        $"/api/Attendance/show-attendance/{Uri.EscapeDataString(person.Qr!)}",
                                        authorization
                                    );

                                var records =
                                    api.Success
                                        ? ParseAttendanceRecords(
                                            api.Raw
                                        )
                                        : new List<AttendanceRecord>();

                                records =
                                    FilterAttendanceByPeriod(
                                        records,
                                        action
                                    );

                                return new PersonAttendanceAggregate
                                {
                                    Person =
                                        person,

                                    Records =
                                        records
                                };
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }
                    )
                    .ToList();

            var result =
                await Task.WhenAll(
                    tasks
                );

            return result.ToList();
        }

        private static List<AttendanceRecord>
            FilterAttendanceByPeriod(
                List<AttendanceRecord> records,
                PersonAction action)
        {
            if (action.AllTime)
            {
                return records;
            }

            if (!action.FromDate.HasValue &&
                !action.ToDate.HasValue)
            {
                return records;
            }

            return records
                .Where(
                    x =>
                        DateInsideRange(
                            x.Date,
                            action.FromDate,
                            action.ToDate
                        )
                )
                .ToList();
        }

        private static List<AttendanceRecord>
            FilterAttendanceByPeriod(
                List<AttendanceRecord> records,
                GroupAction action)
        {
            if (action.AllTime)
            {
                return records;
            }

            if (!action.FromDate.HasValue &&
                !action.ToDate.HasValue)
            {
                return records;
            }

            return records
                .Where(
                    x =>
                        DateInsideRange(
                            x.Date,
                            action.FromDate,
                            action.ToDate
                        )
                )
                .ToList();
        }

        private static bool DateInsideRange(
            DateTime? date,
            DateTime? from,
            DateTime? to)
        {
            if (!date.HasValue)
            {
                return false;
            }

            var value =
                date.Value.Date;

            if (from.HasValue &&
                value <
                from.Value.Date)
            {
                return false;
            }

            if (to.HasValue &&
                value >
                to.Value.Date)
            {
                return false;
            }

            return true;
        }

        // =========================================================
        // SUBSCRIPTIONS
        // =========================================================

        private async Task<IActionResult> ExecuteGroupSubscriptionBalanceReport(
            GradeResult grade,
            GroupAction action,
            ChatSessionState session,
            string authorization,
            string conversationId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userId, out var userIdValue))
            {
                return Unauthorized(new
                {
                    conversationId,
                    message = "تعذر تحديد حساب المستخدم لحساب الاشتراكات."
                });
            }

            var serviceCode = await _db.Users
                .Where(user => user.Id == userIdValue)
                .Select(user => user.ChurchServices.Services.Code)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(serviceCode))
            {
                return StatusCode(503, new
                {
                    conversationId,
                    type = "subscription_settings_unavailable",
                    message = "مش قادر أحدد إعدادات أسعار الاشتراك الخاصة بالخدمة، لذلك مش هعرض إجماليًا ماليًا غير مؤكد."
                });
            }

            SubscriptionServiceSettingsDocument? settings;
            try
            {
                settings = await _settingsService.GetSubscriptionSettingsAsync(
                    serviceCode
                );
            }
            catch
            {
                return StatusCode(503, new
                {
                    conversationId,
                    type = "subscription_settings_unavailable",
                    message = "تعذر تحميل إعدادات أسعار الاشتراك حاليًا، لذلك لم أقدر أحسب إجمالي المبالغ المطلوبة بدقة."
                });
            }

            var gradeKey = grade.Id.ToString(CultureInfo.InvariantCulture);
            if (settings?.Settings?.CalculationStarts == null ||
                !settings.Settings.CalculationStarts.TryGetValue(
                    gradeKey,
                    out var calculationStart
                ) ||
                calculationStart.Year < 2000 ||
                calculationStart.Year > 2100 ||
                calculationStart.Month < 1 ||
                calculationStart.Month > 12)
            {
                return StatusCode(503, new
                {
                    conversationId,
                    type = "subscription_settings_unavailable",
                    message = $"إعدادات بداية حساب الاشتراك غير موجودة لمرحلة {grade.Name}، لذلك مش هعرض إجماليًا تقديريًا على إنه مبلغ مؤكد."
                });
            }

            var today = GetEgyptNow().Date;
            var endMonth = new DateTime(
                action.ToDate!.Value.Year,
                action.ToDate.Value.Month,
                1
            );
            var currentMonth = new DateTime(today.Year, today.Month, 1);
            if (endMonth > currentMonth)
            {
                endMonth = currentMonth;
            }

            var configuredStart = new DateTime(
                calculationStart.Year,
                calculationStart.Month,
                1
            );

            // A single requested month is a balance as of that month,
            // so include all configured months up to it. Explicit
            // multi-month ranges remain limited to the requested range.
            var fromMonth = configuredStart;
            if (action.FromDate.HasValue &&
                action.ToDate.HasValue &&
                (action.FromDate.Value.Year != action.ToDate.Value.Year ||
                 action.FromDate.Value.Month != action.ToDate.Value.Month))
            {
                var requestedStart = new DateTime(
                    action.FromDate.Value.Year,
                    action.FromDate.Value.Month,
                    1
                );
                if (requestedStart > fromMonth)
                {
                    fromMonth = requestedStart;
                }
            }

            var months = endMonth < fromMonth
                ? new List<DateTime>()
                : EnumerateMonths(fromMonth, endMonth);

            if (months.Count > 36)
            {
                return RangeTooLarge(conversationId);
            }

            var peopleResult = await GetPeopleByGrade(grade.Id, authorization);
            if (!peopleResult.Success)
            {
                if (peopleResult.StatusCode == 404)
                {
                    session.LastGroupAction = CloneGroupAction(action);
                    session.PendingGroupAction = null;
                    TouchSession(session);

                    return Ok(new
                    {
                        conversationId,
                        type = "group_subscription_balance",
                        grade = new { id = grade.Id, name = grade.Name },
                        count = 0,
                        answer = $"لا يوجد أفراد مسجلون في {grade.Name}.",
                        data = Array.Empty<object>()
                    });
                }

                return StatusCode(peopleResult.StatusCode, new
                {
                    conversationId,
                    type = "subscription_data_unavailable",
                    message = peopleResult.ErrorMessage
                });
            }

            var roster = peopleResult.People
                .Where(person => person.Id.HasValue)
                .ToDictionary(person => person.Id!.Value);
            var paymentsByStudent = new Dictionary<int, List<SubscriptionPaymentSnapshot>>();

            foreach (var month in months)
            {
                var api = await PostExactLocal(
                    "/api/Subscriptions/show",
                    authorization,
                    new
                    {
                        grade = grade.Id,
                        month = month.Month,
                        year = month.Year
                    }
                );

                if (!api.Success ||
                    !TryParseArray(api.Raw, out var rows))
                {
                    return StatusCode(
                        api.Success ? 502 : api.StatusCode,
                        new
                        {
                            conversationId,
                            type = "subscription_data_unavailable",
                            message = "تعذر تحميل سجلات الاشتراكات كاملة؛ أوقفت التقرير حتى لا أعتبر البيانات الناقصة مبالغ غير مدفوعة."
                        }
                    );
                }

                foreach (var row in rows)
                {
                    var record = ParseSubscriptionRecord(row);
                    if (!record.StudentId.HasValue)
                    {
                        continue;
                    }

                    var studentId = record.StudentId.Value;
                    if (!roster.ContainsKey(studentId))
                    {
                        continue;
                    }

                    if (!paymentsByStudent.TryGetValue(studentId, out var snapshots))
                    {
                        snapshots = new List<SubscriptionPaymentSnapshot>();
                        paymentsByStudent[studentId] = snapshots;
                    }

                    snapshots.Add(new SubscriptionPaymentSnapshot
                    {
                        Year = month.Year,
                        Month = month.Month,
                        IsPaid = record.IsPaid == true,
                        SubscriptionId = record.SubscriptionId,
                        UserName = record.UserName,
                        LastUpdated = record.LastUpdated
                    });
                }
            }

            var reports = new List<GroupSubscriptionMemberReport>();

            foreach (var (studentId, person) in roster)
            {
                paymentsByStudent.TryGetValue(studentId, out var snapshots);
                snapshots ??= new List<SubscriptionPaymentSnapshot>();

                var withoutJob = _subscriptionCalculator.CalculateBalance(
                    settings,
                    grade.Id,
                    hasJob: false,
                    paymentSnapshots: snapshots,
                    requestedFromMonth: GetStudentCalculationStart(
                        person,
                        configuredStart
                    ),
                    requestedToMonth: endMonth,
                    today: today
                );

                var withJob = _subscriptionCalculator.CalculateBalance(
                    settings,
                    grade.Id,
                    hasJob: true,
                    paymentSnapshots: snapshots,
                    requestedFromMonth: GetStudentCalculationStart(
                        person,
                        configuredStart
                    ),
                    requestedToMonth: endMonth,
                    today: today
                );

                var report = new GroupSubscriptionMemberReport
                {
                    Person = person,
                    TotalDue = 0
                };

                foreach (var month in withoutJob.Months)
                {
                    var monthLabel = $"{month.Month}/{month.Year}";
                    if (month.IsPaid)
                    {
                        report.PaidMonths.Add(monthLabel);
                        continue;
                    }

                    report.UnpaidMonths.Add(monthLabel);
                    var employedMonth = withJob.Months.FirstOrDefault(
                        candidate => candidate.Year == month.Year &&
                                     candidate.Month == month.Month
                    );

                    if (employedMonth == null)
                    {
                        report.MissingPriceMonths.Add(monthLabel);
                        report.TotalDue = null;
                        continue;
                    }

                    if (month.HasPriceConfiguration != employedMonth.HasPriceConfiguration ||
                        (month.Amount.HasValue && employedMonth.Amount.HasValue &&
                         month.Amount.Value != employedMonth.Amount.Value))
                    {
                        report.RequiresEmploymentStatus = true;
                        report.TotalDue = null;
                        continue;
                    }

                    if (!month.HasPriceConfiguration ||
                        !month.Amount.HasValue ||
                        !employedMonth.Amount.HasValue)
                    {
                        report.MissingPriceMonths.Add(monthLabel);
                        report.TotalDue = null;
                        continue;
                    }

                    if (report.TotalDue.HasValue)
                    {
                        report.TotalDue += month.Amount.Value;
                    }
                }

                reports.Add(report);
            }

            session.LastGroupAction = CloneGroupAction(action);
            session.PendingGroupAction = null;
            TouchSession(session);

            return Ok(new
            {
                conversationId,
                type = "group_subscription_balance",
                grade = new { id = grade.Id, name = grade.Name },
                period = new
                {
                    fromMonth = fromMonth.ToString("yyyy-MM"),
                    toMonth = endMonth.ToString("yyyy-MM")
                },
                count = reports.Count,
                answer = BuildGroupSubscriptionBalanceAnswer(
                    grade,
                    fromMonth,
                    endMonth,
                    reports
                ),
                data = reports.Select(report => new
                {
                    person = ToBasicPerson(report.Person),
                    paidMonths = report.PaidMonths,
                    unpaidMonths = report.UnpaidMonths,
                    totalDue = report.TotalDue,
                    missingPriceMonths = report.MissingPriceMonths,
                    requiresEmploymentStatus = report.RequiresEmploymentStatus
                })
            });
        }

        private async Task<List<SubscriptionRecord>>
            GetSubscriptionsForMonth(
                int gradeId,
                int month,
                int year,
                string authorization)
        {
            var api =
                await PostExactLocal(
                    "/api/Subscriptions/show",
                    authorization,
                    new
                    {
                        grade =
                            gradeId,

                        month,

                        year
                    }
                );

            if (!api.Success)
            {
                return new List<SubscriptionRecord>();
            }

            return ParseSubscriptionRecords(
                api.Raw
            );
        }

        private static List<SubscriptionRecord>
            FilterSubscriptionsByPayment(
                List<SubscriptionRecord> records,
                string status)
        {
            status =
                NormalizePaymentStatus(
                    status
                );

            return status switch
            {
                "paid" =>
                    records
                        .Where(
                            x =>
                                x.IsPaid ==
                                true
                        )
                        .ToList(),

                "unpaid" =>
                    records
                        .Where(
                            x =>
                                x.IsPaid ==
                                false
                        )
                        .ToList(),

                _ =>
                    records
            };
        }

        // =========================================================
        // VISITATIONS
        // =========================================================

        private async Task<List<VisitationRecord>>
            GetVisitationsForRange(
                int gradeId,
                DateTime from,
                DateTime to,
                string authorization)
        {
            var months =
                EnumerateMonths(
                    from,
                    to
                );

            if (months.Count > 36)
            {
                return new List<VisitationRecord>();
            }

            var all =
                new List<VisitationRecord>();

            foreach (var month in months)
            {
                var api =
                    await PostExactLocal(
                        "/api/Visitation/show",
                        authorization,
                        new
                        {
                            grade =
                                gradeId,

                            month =
                                month.Month,

                            year =
                                month.Year
                        }
                    );

                if (!api.Success)
                {
                    continue;
                }

                all.AddRange(
                    ParseVisitationRecords(
                        api.Raw
                    )
                );
            }

            return all
                .Where(
                    x =>
                        DateInsideRange(
                            x.Date,
                            from,
                            to
                        )
                )
                .GroupBy(
                    x =>
                        x.Id
                        ??
                        0
                )
                .Select(
                    x =>
                        x.First()
                )
                .OrderByDescending(
                    x =>
                        x.Date
                )
                .ToList();
        }

        // =========================================================
        // PARSERS
        // =========================================================

        private static List<AttendanceRecord>
            ParseAttendanceRecords(
                string raw)
        {
            var result =
                new List<AttendanceRecord>();

            if (!TryParseArray(
                    raw,
                    out var items))
            {
                return result;
            }

            foreach (var item in items)
            {
                result.Add(
                    new AttendanceRecord
                    {
                        Date =
                            GetNullableDate(
                                item,
                                "date"
                            ),

                        Status =
                            GetNullableInt(
                                item,
                                "status"
                            ),

                        Comment =
                            GetNullableString(
                                item,
                                "comment"
                            ),

                        Excused =
                            GetNullableString(
                                item,
                                "excused"
                            ),

                        LastUpdated =
                            GetNullableDate(
                                item,
                                "lastUpdated"
                            ),

                        UserName =
                            GetNullableString(
                                item,
                                "userName"
                            )
                    }
                );
            }

            return result;
        }

        private static List<SubscriptionRecord>
            ParseSubscriptionRecords(
                string raw)
        {
            var result =
                new List<SubscriptionRecord>();

            if (!TryParseArray(
                    raw,
                    out var items))
            {
                return result;
            }

            foreach (var item in items)
            {
                result.Add(ParseSubscriptionRecord(item));
            }

            return result;
        }

        private static SubscriptionRecord ParseSubscriptionRecord(
            JsonElement item)
        {
            return new SubscriptionRecord
            {
                StudentId = GetNullableInt(item, "studentId"),
                StudentQr = GetNullableString(item, "studentQr"),
                StudentName = GetNullableString(item, "studentName"),
                Excused = GetNullableString(item, "excused"),
                IsPaid = GetNullableBool(item, "isPaid"),
                SubscriptionId = GetNullableInt(item, "subscriptionId"),
                LastUpdated = GetNullableDate(item, "lastUpdated"),
                UserName = GetNullableString(item, "userName")
            };
        }

        private static List<VisitationRecord>
            ParseVisitationRecords(
                string raw)
        {
            var result =
                new List<VisitationRecord>();

            if (!TryParseArray(
                    raw,
                    out var items))
            {
                return result;
            }

            foreach (var item in items)
            {
                result.Add(
                    new VisitationRecord
                    {
                        Id =
                            GetNullableInt(
                                item,
                                "id"
                            ),

                        StudentId =
                            GetNullableInt(
                                item,
                                "studentId"
                            ),

                        StudentQr =
                            GetNullableString(
                                item,
                                "studentQr"
                            ),

                        StudentName =
                            GetNullableString(
                                item,
                                "studentName"
                            ),

                        Excused =
                            GetNullableString(
                                item,
                                "excused"
                            ),

                        Date =
                            GetNullableDate(
                                item,
                                "date"
                            ),

                        Comment =
                            GetNullableString(
                                item,
                                "comment"
                            ),

                        VisitedBy =
                            GetNullableString(
                                item,
                                "visitedBy"
                            )
                    }
                );
            }

            return result;
        }

        private static bool TryParseArray(
            string raw,
            out List<JsonElement> items)
        {
            items =
                new List<JsonElement>();

            try
            {
                using var document =
                    JsonDocument.Parse(
                        raw
                    );

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Array)
                {
                    return false;
                }

                foreach (
                    var item
                    in document.RootElement
                        .EnumerateArray())
                {
                    items.Add(
                        item.Clone()
                    );
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // =========================================================
        // PERSONS / GRADES
        // =========================================================

        private async Task<GradesResult>
            GetGrades(
                string authorization)
        {
            var api =
                await GetExactLocal(
                    "/api/Grades/show",
                    authorization
                );

            if (!api.Success)
            {
                return new GradesResult
                {
                    Success =
                        false,

                    StatusCode =
                        api.StatusCode,

                    ErrorMessage =
                        "تعذر تحميل المراحل."
                };
            }

            try
            {
                using var document =
                    JsonDocument.Parse(
                        api.Raw
                    );

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Array)
                {
                    throw new JsonException();
                }

                var grades =
                    document.RootElement
                        .EnumerateArray()
                        .Where(
                            x =>
                                x.TryGetProperty(
                                    "id",
                                    out _
                                )
                                &&
                                x.TryGetProperty(
                                    "name",
                                    out _
                                )
                        )
                        .Select(
                            x =>
                                new GradeResult
                                {
                                    Id =
                                        x.GetProperty(
                                            "id"
                                        )
                                        .GetInt32(),

                                    Name =
                                        x.GetProperty(
                                            "name"
                                        )
                                        .GetString()
                                        ??
                                        ""
                                }
                        )
                        .ToList();

                return new GradesResult
                {
                    Success =
                        true,

                    StatusCode =
                        200,

                    Grades =
                        grades
                };
            }
            catch
            {
                return new GradesResult
                {
                    Success =
                        false,

                    StatusCode =
                        500,

                    ErrorMessage =
                        "تعذر قراءة بيانات المراحل."
                };
            }
        }

        private async Task<PeopleResult>
            GetPeopleByGrade(
                int gradeId,
                string authorization)
        {
            var api =
                await GetExactLocal(
                    $"/api/Students/show-students/{gradeId}",
                    authorization
                );

            if (!api.Success)
            {
                return new PeopleResult
                {
                    Success =
                        false,

                    StatusCode =
                        api.StatusCode,

                    ErrorMessage =
                        "تعذر الوصول إلى بيانات الأفراد."
                };
            }

            if (!TryParseArray(
                    api.Raw,
                    out var items))
            {
                return new PeopleResult
                {
                    Success =
                        false,

                    StatusCode =
                        500,

                    ErrorMessage =
                        "تعذر قراءة بيانات الأفراد."
                };
            }

            return new PeopleResult
            {
                Success =
                    true,

                StatusCode =
                    200,

                People =
                    items
                        .Select(
                            ParsePerson
                        )
                        .ToList()
            };
        }

        private async Task<List<PersonResult>>
            FindPeopleByName(
                string name,
                List<GradeResult> grades,
                string authorization)
        {
            var query =
                NormalizeArabic(
                    name
                );

            var exact =
                new List<PersonResult>();

            var partial =
                new List<PersonResult>();

            foreach (var grade in grades)
            {
                var people =
                    await GetPeopleByGrade(
                        grade.Id,
                        authorization
                    );

                if (!people.Success)
                {
                    continue;
                }

                foreach (
                    var person
                    in people.People)
                {
                    if (string.IsNullOrWhiteSpace(
                        person.Name))
                    {
                        continue;
                    }

                    var normalizedName =
                        NormalizeArabic(
                            person.Name
                        );

                    if (normalizedName ==
                        query)
                    {
                        exact.Add(
                            person
                        );
                    }
                    else if (
                        normalizedName.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        partial.Add(
                            person
                        );
                    }
                }
            }

            var results =
                exact.Count > 0
                    ? exact
                    : partial;

            return results
                .GroupBy(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.Qr
                        )
                            ? x.Qr!
                            : $"ID-{x.Id}"
                )
                .Select(
                    x =>
                        x.First()
                )
                .ToList();
        }

        private async Task<PersonResult?>
            GetPersonForSelectionByQr(
                string qr,
                string authorization)
        {
            var basicApi =
                await GetExactLocal(
                    $"/api/Students/show-student/{Uri.EscapeDataString(qr)}",
                    authorization
                );

            PersonResult? basic =
                basicApi.Success
                    ? TryParseSinglePerson(
                        basicApi.Raw
                    )
                    : null;

            var detailsApi =
                await GetExactLocal(
                    $"/api/Students/show-student-details/{Uri.EscapeDataString(qr)}",
                    authorization
                );

            PersonResult? details =
                detailsApi.Success
                    ? TryParseSinglePerson(
                        detailsApi.Raw
                    )
                    : null;

            var result =
                MergePersons(
                    basic,
                    details
                );

            if (result != null &&
                string.IsNullOrWhiteSpace(
                    result.Qr))
            {
                result.Qr =
                    qr;
            }

            return result;
        }

        private async Task<PersonResult>
            EnsurePersonHasGrade(
                PersonResult person,
                string authorization)
        {
            if (person.Grade.HasValue)
            {
                return person;
            }

            if (string.IsNullOrWhiteSpace(
                person.Qr))
            {
                return person;
            }

            var refreshed =
                await GetPersonForSelectionByQr(
                    person.Qr,
                    authorization
                );

            return refreshed
                ??
                person;
        }

        private static PersonResult ParsePerson(
            JsonElement item)
        {
            return new PersonResult
            {
                Id =
                    GetNullableInt(
                        item,
                        "id"
                    ),

                Qr =
                    GetNullableString(
                        item,
                        "qr"
                    )
                    ??
                    GetNullableString(
                        item,
                        "studentQr"
                    ),

                Name =
                    GetNullableString(
                        item,
                        "name"
                    )
                    ??
                    GetNullableString(
                        item,
                        "studentName"
                    ),

                NameEnglish =
                    GetNullableString(
                        item,
                        "nameEnglish"
                    )
                    ??
                    GetNullableString(
                        item,
                        "nameEn"
                    ),

                Grade =
                    GetNullableInt(
                        item,
                        "grade"
                    ),

                Phone =
                    GetNullableString(
                        item,
                        "phone"
                    ),

                AnotherPhone =
                    GetNullableString(
                        item,
                        "anotherPhone"
                    ),

                Address =
                    GetNullableString(
                        item,
                        "address"
                    ),

                Area =
                    GetNullableString(
                        item,
                        "area"
                    ),

                Location =
                    GetNullableString(
                        item,
                        "location"
                    ),

                Notes =
                    GetNullableString(
                        item,
                        "notes"
                    ),

                Excused =
                    GetNullableString(
                        item,
                        "excused"
                    ),

                Role =
                    GetNullableString(
                        item,
                        "role"
                    ),

                Details =
                    GetNullableString(
                        item,
                        "details"
                    ),

                RoleId =
                    GetNullableInt(
                        item,
                        "roleId"
                    ),

                DateOfBirth =
                    GetNullableString(
                        item,
                        "dateOfBirth"
                    ),

                Gender =
                    GetNullableInt(
                        item,
                        "gender"
                    ),

                Confessor =
                    GetNullableString(
                        item,
                        "confessor"
                    ),

                CreatedAt =
                    GetNullableString(
                        item,
                        "createdAt"
                    ),

                CreatedBy =
                    GetNullableString(
                        item,
                        "createdBy"
                    ),

                UpdatedBy =
                    GetNullableString(
                        item,
                        "updatedBy"
                    )
            };
        }

        private static PersonResult?
            TryParseSinglePerson(
                string raw)
        {
            try
            {
                using var document =
                    JsonDocument.Parse(
                        raw
                    );

                if (document.RootElement.ValueKind ==
                    JsonValueKind.Object)
                {
                    return ParsePerson(
                        document.RootElement
                    );
                }

                if (document.RootElement.ValueKind ==
                        JsonValueKind.Array
                    &&
                    document.RootElement
                        .GetArrayLength() >
                    0)
                {
                    return ParsePerson(
                        document.RootElement[0]
                    );
                }
            }
            catch
            {
            }

            return null;
        }

        private static PersonResult? MergePersons(
            PersonResult? first,
            PersonResult? second)
        {
            if (first == null)
            {
                return second;
            }

            if (second == null)
            {
                return first;
            }

            return new PersonResult
            {
                Id =
                    second.Id ??
                    first.Id,

                Qr =
                    Pick(
                        second.Qr,
                        first.Qr
                    ),

                Name =
                    Pick(
                        second.Name,
                        first.Name
                    ),

                NameEnglish =
                    Pick(
                        second.NameEnglish,
                        first.NameEnglish
                    ),

                Grade =
                    second.Grade ??
                    first.Grade,

                Phone =
                    Pick(
                        second.Phone,
                        first.Phone
                    ),

                AnotherPhone =
                    Pick(
                        second.AnotherPhone,
                        first.AnotherPhone
                    ),

                Address =
                    Pick(
                        second.Address,
                        first.Address
                    ),

                Area =
                    Pick(
                        second.Area,
                        first.Area
                    ),

                Location =
                    Pick(
                        second.Location,
                        first.Location
                    ),

                Notes =
                    Pick(
                        second.Notes,
                        first.Notes
                    ),

                Excused =
                    Pick(
                        second.Excused,
                        first.Excused
                    ),

                Role =
                    Pick(
                        second.Role,
                        first.Role
                    ),

                Details =
                    Pick(
                        second.Details,
                        first.Details
                    ),

                RoleId =
                    second.RoleId ??
                    first.RoleId,

                DateOfBirth =
                    Pick(
                        second.DateOfBirth,
                        first.DateOfBirth
                    ),

                Gender =
                    second.Gender ??
                    first.Gender,

                Confessor =
                    Pick(
                        second.Confessor,
                        first.Confessor
                    ),

                CreatedAt =
                    Pick(
                        second.CreatedAt,
                        first.CreatedAt
                    ),

                CreatedBy =
                    Pick(
                        second.CreatedBy,
                        first.CreatedBy
                    ),

                UpdatedBy =
                    Pick(
                        second.UpdatedBy,
                        first.UpdatedBy
                    )
            };
        }

        private static string? Pick(
            string? first,
            string? second)
        {
            return !string.IsNullOrWhiteSpace(
                first
            )
                ? first
                : second;
        }

        // =========================================================
        // RESPONSE BUILDERS
        // =========================================================

        private static string BuildAbsenceAnswer(
            PersonResult person,
            List<AttendanceRecord> records,
            PersonAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الغياب — {person.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine(
                $"عدد مرات الغياب: {records.Count}"
            );

            if (records.Count == 0)
            {
                return builder.ToString();
            }

            builder.AppendLine();

            foreach (var record in records)
            {
                builder.AppendLine(
                    $"• {FormatDate(record.Date)}"
                );

                builder.AppendLine(
                    $"العذر: {(
                        string.IsNullOrWhiteSpace(
                            record.Excused
                        )
                            ? "لا يوجد عذر مكتوب"
                            : record.Excused
                    )}"
                );

                builder.AppendLine(
                    $"التعليق: {(
                        string.IsNullOrWhiteSpace(
                            record.Comment
                        )
                            ? "لا يوجد تعليق مكتوب"
                            : record.Comment
                    )}"
                );

                if (action.IncludeAudit)
                {
                    builder.AppendLine(
                        $"تم التسجيل بواسطة: {SafeText(record.UserName)}"
                    );

                    builder.AppendLine(
                        $"تاريخ التسجيل/آخر تحديث: {FormatDateTime(record.LastUpdated)}"
                    );
                }

                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static string BuildPresentAnswer(
            PersonResult person,
            List<AttendanceRecord> records,
            PersonAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الحضور — {person.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine(
                $"عدد مرات الحضور: {records.Count}"
            );

            if (records.Count == 0)
            {
                return builder.ToString();
            }

            builder.AppendLine();

            foreach (var record in records)
            {
                builder.AppendLine(
                    $"• {FormatDate(record.Date)}"
                );

                if (!string.IsNullOrWhiteSpace(
                    record.Comment))
                {
                    builder.AppendLine(
                        $"التعليق: {record.Comment}"
                    );
                }

                if (action.IncludeAudit)
                {
                    builder.AppendLine(
                        $"تم التسجيل بواسطة: {SafeText(record.UserName)}"
                    );

                    builder.AppendLine(
                        $"تاريخ التسجيل/آخر تحديث: {FormatDateTime(record.LastUpdated)}"
                    );
                }

                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static string BuildGroupAttendanceAnswer(
            GradeResult grade,
            List<PersonAttendanceAggregate> matches,
            GroupAction action)
        {
            var isAbsence =
                action.Kind ==
                "absence";

            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"{(isAbsence ? "الغياب" : "الحضور")} — {grade.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine(
                $"عدد الأفراد: {matches.Count}"
            );

            builder.AppendLine(
                $"إجمالي مرات {(isAbsence ? "الغياب" : "الحضور")}: {matches.Sum(x => x.Records.Count)}"
            );

            if (matches.Count == 0)
            {
                return builder.ToString();
            }

            builder.AppendLine();

            var display =
                matches.Take(40).ToList();

            for (var i = 0; i < display.Count; i++)
            {
                builder.AppendLine(
                    $"• {display[i].Person.Name} — عدد المرات: {display[i].Records.Count}"
                );
            }

            if (matches.Count > display.Count)
            {
                builder.AppendLine();

                builder.AppendLine(
                    $"وهناك {matches.Count - display.Count} فرد إضافي في البيانات."
                );
            }

            return builder.ToString();
        }

        private static string BuildDirectPersonSubscriptionAnswer(
            PersonResult person,
            List<SubscriptionRecord> records,
            PersonAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الاشتراكات — {person.Name}"
            );

            builder.AppendLine(
                $"عدد السجلات: {records.Count}"
            );

            builder.AppendLine();

            foreach (var record in records)
            {
                builder.AppendLine(
                    record.IsPaid == true
                        ? "الحالة: تم دفع الاشتراك"
                        : "الحالة: لم يتم دفع الاشتراك"
                );

                if (action.IncludeAudit &&
                    record.IsPaid == true)
                {
                    builder.AppendLine(
                        $"تم التسجيل بواسطة: {SafeText(record.UserName)}"
                    );

                    builder.AppendLine(
                        $"تاريخ التسجيل/آخر تحديث: {FormatDateTime(record.LastUpdated)}"
                    );
                }

                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static string BuildPersonSubscriptionPeriodAnswer(
            PersonResult person,
            List<SubscriptionMonthResult> periods,
            PersonAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الاشتراكات — {person.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine();

            foreach (var period in periods)
            {
                var record =
                    period.Records.FirstOrDefault();

                if (record == null)
                {
                    if (action.PaymentStatus == "all")
                    {
                        builder.AppendLine(
                            $"{period.Month}/{period.Year}: لا توجد نتيجة"
                        );
                    }

                    continue;
                }

                builder.AppendLine(
                    $"{period.Month}/{period.Year}: {(record.IsPaid == true ? "مدفوع" : "غير مدفوع")}"
                );

                if (action.IncludeAudit &&
                    record.IsPaid == true)
                {
                    builder.AppendLine(
                        $"بواسطة: {SafeText(record.UserName)}"
                    );

                    builder.AppendLine(
                        $"آخر تحديث: {FormatDateTime(record.LastUpdated)}"
                    );
                }
            }

            return builder.ToString().Trim();
        }

        private static string BuildGroupSubscriptionsAnswer(
            GradeResult grade,
            List<SubscriptionMonthResult> periods,
            GroupAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الاشتراكات — {grade.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine();

            foreach (var period in periods)
            {
                var paid =
                    period.Records.Count(
                        x =>
                            x.IsPaid ==
                            true
                    );

                var unpaid =
                    period.Records.Count(
                        x =>
                            x.IsPaid ==
                            false
                    );

                builder.AppendLine(
                    $"{period.Month}/{period.Year} — النتائج: {period.Records.Count}"
                );

                if (action.PaymentStatus ==
                    "all")
                {
                    builder.AppendLine(
                        $"مدفوع: {paid} | غير مدفوع: {unpaid}"
                    );
                }
                else if (action.PaymentStatus == "paid")
                {
                    builder.AppendLine(
                        $"المعروض: المدفوع فقط — العدد: {period.Records.Count}"
                    );
                }
                else
                {
                    builder.AppendLine(
                        $"المعروض: غير المدفوع فقط — العدد: {period.Records.Count}"
                    );
                }

                var show =
                    period.Records
                        .Take(25)
                        .ToList();

                foreach (var record in show)
                {
                    builder.AppendLine(
                        $"• {record.StudentName} — {(record.IsPaid == true ? "مدفوع" : "غير مدفوع")}"
                    );
                }

                if (period.Records.Count >
                    show.Count)
                {
                    builder.AppendLine(
                        $"... و{period.Records.Count - show.Count} نتيجة إضافية"
                    );
                }

                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static string BuildGroupSubscriptionBalanceAnswer(
            GradeResult grade,
            DateTime fromMonth,
            DateTime toMonth,
            List<GroupSubscriptionMemberReport> reports)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"تقرير اشتراكات {grade.Name}");
            builder.AppendLine(
                $"الفترة المحسوبة: {fromMonth:MM/yyyy} إلى {toMonth:MM/yyyy}"
            );
            builder.AppendLine($"عدد الأفراد: {reports.Count}");

            if (reports.Count == 0)
            {
                builder.AppendLine("لا يوجد أفراد أو سجلات اشتراك في الفترة المحسوبة.");
                return builder.ToString().Trim();
            }

            builder.AppendLine();

            foreach (var report in reports.OrderBy(x => x.Person.Name))
            {
                builder.AppendLine($"• {SafeText(report.Person.Name)}");
                builder.AppendLine(
                    $"  الأشهر المدفوعة: {FormatMonthList(report.PaidMonths)}"
                );
                builder.AppendLine(
                    $"  الأشهر غير المدفوعة: {FormatMonthList(report.UnpaidMonths)}"
                );

                if (report.TotalDue.HasValue)
                {
                    builder.AppendLine(
                        $"  إجمالي المطلوب: {report.TotalDue.Value:0.##}"
                    );
                }
                else if (report.RequiresEmploymentStatus)
                {
                    builder.AppendLine(
                        "  إجمالي المطلوب: غير محسوب؛ السعر يعتمد على وجود عمل ولا توجد حالة عمل مسجلة للشخص."
                    );
                }
                else if (report.MissingPriceMonths.Count > 0)
                {
                    builder.AppendLine(
                        $"  إجمالي المطلوب: غير مكتمل؛ لا يوجد سعر مضبوط للأشهر {FormatMonthList(report.MissingPriceMonths)}."
                    );
                }
                else
                {
                    builder.AppendLine("  إجمالي المطلوب: 0");
                }
            }

            return builder.ToString().Trim();
        }

        private static string FormatMonthList(List<string> months) =>
            months.Count == 0 ? "لا يوجد" : string.Join("، ", months);

        private static string BuildPersonVisitationAnswer(
            PersonResult person,
            List<VisitationRecord> visits,
            PersonAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الزيارات — {person.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine(
                $"عدد الزيارات: {visits.Count}"
            );

            builder.AppendLine();

            foreach (var visit in visits)
            {
                builder.AppendLine(
                    $"• {FormatDate(visit.Date)}"
                );

                builder.AppendLine(
                    $"التعليق: {(
                        string.IsNullOrWhiteSpace(
                            visit.Comment
                        )
                            ? "لا يوجد تعليق"
                            : visit.Comment
                    )}"
                );

                if (!string.IsNullOrWhiteSpace(
                    visit.Excused))
                {
                    builder.AppendLine(
                        $"العذر المسجل على الشخص: {visit.Excused}"
                    );
                }

                builder.AppendLine(
                    $"تمت الزيارة بواسطة: {SafeText(visit.VisitedBy)}"
                );

                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static string BuildGroupVisitationAnswer(
            GradeResult grade,
            List<VisitationRecord> visits,
            GroupAction action)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"الزيارات — {grade.Name}"
            );

            builder.AppendLine(
                $"الفترة: {FormatPeriod(action)}"
            );

            builder.AppendLine(
                $"عدد الزيارات: {visits.Count}"
            );

            var peopleCount =
                visits
                    .Select(
                        x =>
                            x.StudentQr
                    )
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(
                                x
                            )
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Count();

            builder.AppendLine(
                $"عدد الأفراد الذين تمت زيارتهم: {peopleCount}"
            );

            builder.AppendLine();

            var show =
                visits
                    .Take(35)
                    .ToList();

            foreach (var visit in show)
            {
                builder.AppendLine(
                    $"• {visit.StudentName} — {FormatDate(visit.Date)}"
                );

                if (!string.IsNullOrWhiteSpace(
                    visit.Comment))
                {
                    builder.AppendLine(
                        $"  {visit.Comment}"
                    );
                }
            }

            if (visits.Count >
                show.Count)
            {
                builder.AppendLine();

                builder.AppendLine(
                    $"وهناك {visits.Count - show.Count} زيارة إضافية في البيانات."
                );
            }

            return builder.ToString();
        }

        private static string BuildPersonDetailsAnswer(
            PersonResult person)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"بيانات {person.Name}"
            );

            AppendIfValue(
                builder,
                "QR",
                person.Qr
            );

            AppendIfValue(
                builder,
                "الاسم بالإنجليزية",
                person.NameEnglish
            );

            AppendIfValue(
                builder,
                "التليفون",
                person.Phone
            );

            AppendIfValue(
                builder,
                "تليفون آخر",
                person.AnotherPhone
            );

            AppendIfValue(
                builder,
                "العنوان",
                person.Address
            );

            AppendIfValue(
                builder,
                "المنطقة",
                person.Area
            );

            AppendIfValue(
                builder,
                "الموقع",
                person.Location
            );

            AppendIfValue(
                builder,
                "العذر المسجل",
                person.Excused
            );

            AppendIfValue(
                builder,
                "الوظيفة/الدور",
                person.Role
            );

            if (person.RoleId.HasValue)
            {
                ServantsRolesStatic.TryGetValue(
                    person.RoleId.Value,
                    out var roleName
                );

                builder.AppendLine(
                    $"دور الخادم: {roleName ?? person.RoleId.Value.ToString()}"
                );
            }

            AppendIfValue(
                builder,
                "التفاصيل",
                person.Details
            );

            AppendIfValue(
                builder,
                "الملاحظات",
                person.Notes
            );

            AppendIfValue(
                builder,
                "تاريخ الميلاد",
                person.DateOfBirth
            );

            AppendIfValue(
                builder,
                "أب الاعتراف",
                person.Confessor
            );

            return builder.ToString().Trim();
        }

        private static void AppendIfValue(
            StringBuilder builder,
            string title,
            string? value)
        {
            if (!string.IsNullOrWhiteSpace(
                value))
            {
                builder.AppendLine(
                    $"{title}: {value}"
                );
            }
        }

        // =========================================================
        // PERIOD HELPERS
        // =========================================================

        private static bool HasPeriod(
            PersonAction action)
        {
            return action.AllTime
                ||
                action.FromDate.HasValue
                ||
                action.ToDate.HasValue;
        }

        private static bool HasPeriod(
            GroupAction action)
        {
            return action.AllTime
                ||
                action.FromDate.HasValue
                ||
                action.ToDate.HasValue;
        }

        private static bool HasBoundedPeriod(
            PersonAction action)
        {
            return !action.AllTime
                &&
                action.FromDate.HasValue
                &&
                action.ToDate.HasValue;
        }

        private static bool HasBoundedPeriod(
            GroupAction action)
        {
            return !action.AllTime
                &&
                action.FromDate.HasValue
                &&
                action.ToDate.HasValue;
        }

        private static List<DateTime> EnumerateMonths(
            DateTime from,
            DateTime to)
        {
            if (from > to)
            {
                (
                    from,
                    to
                )
                =
                (
                    to,
                    from
                );
            }

            var result =
                new List<DateTime>();

            var current =
                new DateTime(
                    from.Year,
                    from.Month,
                    1
                );

            var end =
                new DateTime(
                    to.Year,
                    to.Month,
                    1
                );

            while (current <= end)
            {
                result.Add(
                    current
                );

                current =
                    current.AddMonths(1);
            }

            return result;
        }

        private static object ToPeriodObject(
            PersonAction action)
        {
            return new
            {
                allTime =
                    action.AllTime,

                fromDate =
                    action.FromDate?.ToString(
                        "yyyy-MM-dd"
                    ),

                toDate =
                    action.ToDate?.ToString(
                        "yyyy-MM-dd"
                    )
            };
        }

        private static object ToPeriodObject(
            GroupAction action)
        {
            return new
            {
                allTime =
                    action.AllTime,

                fromDate =
                    action.FromDate?.ToString(
                        "yyyy-MM-dd"
                    ),

                toDate =
                    action.ToDate?.ToString(
                        "yyyy-MM-dd"
                    )
            };
        }

        private static string FormatPeriod(
            PersonAction action)
        {
            if (action.AllTime)
            {
                return "كل المدة";
            }

            if (action.FromDate.HasValue &&
                action.ToDate.HasValue)
            {
                if (action.FromDate.Value.Date ==
                    action.ToDate.Value.Date)
                {
                    return action.FromDate
                        .Value
                        .ToString(
                            "yyyy-MM-dd"
                        );
                }

                return
                    $"{action.FromDate.Value:yyyy-MM-dd} إلى {action.ToDate.Value:yyyy-MM-dd}";
            }

            return "غير محددة";
        }

        private static string FormatPeriod(
            GroupAction action)
        {
            if (action.AllTime)
            {
                return "كل المدة";
            }

            if (action.FromDate.HasValue &&
                action.ToDate.HasValue)
            {
                return
                    $"{action.FromDate.Value:yyyy-MM-dd} إلى {action.ToDate.Value:yyyy-MM-dd}";
            }

            return "غير محددة";
        }

        // =========================================================
        // TOOL DEFINITIONS
        // =========================================================

        private static object[] BuildTools(
            int[] gradeIds,
            int[] roleIds)
        {
            return new object[]
            {
                SimpleTool(
                    "get_grades",
                    "Get current dynamic grades/stages/groups."
                ),

                SimpleTool(
                    "get_churches",
                    "Get churches available to the logged in user."
                ),

                SimpleTool(
                    "get_services",
                    "Get services available to the logged in user."
                ),

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_church_services",

                        description =
                            "Get services for a church.",

                        parameters = new
                        {
                            type =
                                "object",

                            properties = new
                            {
                                church_id = new
                                {
                                    type =
                                        "integer"
                                }
                            },

                            required =
                                new[]
                                {
                                    "church_id"
                                },

                            additionalProperties =
                                false
                        }
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_students_by_grade",

                        description =
                            "Get all people in a specific grade/group. Only set grade_id when user explicitly specified a grade.",

                        parameters =
                            GradeOptionalSchema(
                                gradeIds
                            )
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "find_person_by_name",

                        description =
                            "Find a person when user mentions a person's name.",

                        parameters =
                            NameSchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_person_details_by_name",

                        description =
                            "Get details of a named person.",

                        parameters =
                            NameSchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_person_attendance_by_name",

                        description =
                            "Get attendance for a named person, optionally over a period.",

                        parameters =
                            PersonPeriodSchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_person_absence_by_name",

                        description =
                            "Get absence for a named person, optionally over a period.",

                        parameters =
                            PersonPeriodSchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_person_subscriptions_by_name",

                        description =
                            "Get subscriptions/payment status for a named person.",

                        parameters =
                            PersonPeriodSchema(
                                includePaymentStatus: true
                            )
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_person_visitations_by_name",

                        description =
                            "Get visitations for a named person. Period is normally needed.",

                        parameters =
                            PersonPeriodSchema()
                    }
                },

                SimpleTool(
                    "get_selected_person_summary",
                    "Get summary of current selected person."
                ),

                SimpleTool(
                    "get_selected_person_details",
                    "Get details for current selected person."
                ),

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_selected_person_attendance",

                        description =
                            "Get attendance for current selected person.",

                        parameters =
                            PeriodOnlySchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_selected_person_absence",

                        description =
                            "Get absence for current selected person.",

                        parameters =
                            PeriodOnlySchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_selected_person_subscriptions",

                        description =
                            "Get subscriptions for current selected person.",

                        parameters =
                            PeriodOnlySchema(
                                includePaymentStatus: true
                            )
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_selected_person_visitations",

                        description =
                            "Get visitations for current selected person.",

                        parameters =
                            PeriodOnlySchema()
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_group_attendance",

                        description =
                            "Get people who attended in a grade during a period. If grade was not explicitly provided, DO NOT invent grade_id.",

                        parameters =
                            GroupPeriodSchema(
                                gradeIds
                            )
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_group_absence",

                        description =
                            "Get people who were absent in a grade during a period. If grade was not explicitly provided, omit grade_id.",

                        parameters =
                            GroupPeriodSchema(
                                gradeIds
                            )
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_group_subscriptions",

                        description =
                            "Get subscription/payment information for a grade during a period. If grade is missing, omit grade_id.",

                        parameters =
                            GroupPeriodSchema(
                                gradeIds,
                                includePaymentStatus: true
                            )
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_group_visitations",

                        description =
                            "Get visitations for a grade during a period. If grade is missing, omit grade_id.",

                        parameters =
                            GroupPeriodSchema(
                                gradeIds
                            )
                    }
                },

                SimpleTool(
                    "get_servant_roles",
                    "Get the static servant roles."
                ),

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "get_servants_by_role",

                        description =
                            "Get servants having a specific servant role.",

                        parameters = new
                        {
                            type =
                                "object",

                            properties = new
                            {
                                role_id = new
                                {
                                    type =
                                        "integer",

                                    @enum =
                                        roleIds
                                }
                            },

                            required =
                                new[]
                                {
                                    "role_id"
                                },

                            additionalProperties =
                                false
                        }
                    }
                },

                new
                {
                    type = "function",
                    function = new
                    {
                        name =
                            "search_people_by_field",

                        description =
                            "Search people using a person field inside one grade.",

                        parameters = new
                        {
                            type =
                                "object",

                            properties = new
                            {
                                grade_id = new
                                {
                                    type =
                                        "integer",

                                    @enum =
                                        gradeIds
                                },

                                field = new
                                {
                                    type =
                                        "string",

                                    @enum =
                                        AllowedSearchFields.ToArray()
                                },

                                mode = new
                                {
                                    type =
                                        "string",

                                    @enum = new[]
                                    {
                                        "contains",
                                        "equals",
                                        "has_value",
                                        "is_empty"
                                    }
                                },

                                value = new
                                {
                                    type =
                                        "string"
                                }
                            },

                            required = new[]
                            {
                                "field",
                                "mode",
                                "value"
                            },

                            additionalProperties =
                                false
                        }
                    }
                }
            };
        }

        private static object SimpleTool(
            string name,
            string description)
        {
            return new
            {
                type =
                    "function",

                function = new
                {
                    name,

                    description,

                    parameters = new
                    {
                        type =
                            "object",

                        properties =
                            new { },

                        additionalProperties =
                            false
                    }
                }
            };
        }

        private static object NameSchema()
        {
            return new
            {
                type =
                    "object",

                properties = new
                {
                    name = new
                    {
                        type =
                            "string"
                    }
                },

                required =
                    new[]
                    {
                        "name"
                    },

                additionalProperties =
                    false
            };
        }

        private static object GradeOptionalSchema(
            int[] gradeIds)
        {
            return new
            {
                type =
                    "object",

                properties = new
                {
                    grade_id = new
                    {
                        type =
                            "integer",

                        @enum =
                            gradeIds
                    }
                },

                additionalProperties =
                    false
            };
        }

        private static object PersonPeriodSchema(
            bool includePaymentStatus = false)
        {
            var properties =
                new Dictionary<string, object>
                {
                    ["name"] = new
                    {
                        type =
                            "string"
                    },

                    ["from_date"] = new
                    {
                        type =
                            "string",

                        description =
                            "YYYY-MM-DD"
                    },

                    ["to_date"] = new
                    {
                        type =
                            "string",

                        description =
                            "YYYY-MM-DD"
                    },

                    ["all_time"] = new
                    {
                        type =
                            "boolean"
                    },

                    ["include_audit"] = new
                    {
                        type =
                            "boolean"
                    }
                };

            if (includePaymentStatus)
            {
                properties["payment_status"] =
                    new
                    {
                        type =
                            "string",

                        @enum =
                            new[]
                            {
                                "all",
                                "paid",
                                "unpaid"
                            }
                    };
            }

            return new
            {
                type =
                    "object",

                properties,

                required =
                    new[]
                    {
                        "name"
                    },

                additionalProperties =
                    false
            };
        }

        private static object PeriodOnlySchema(
            bool includePaymentStatus = false)
        {
            var properties =
                new Dictionary<string, object>
                {
                    ["from_date"] = new
                    {
                        type =
                            "string",

                        description =
                            "YYYY-MM-DD"
                    },

                    ["to_date"] = new
                    {
                        type =
                            "string",

                        description =
                            "YYYY-MM-DD"
                    },

                    ["all_time"] = new
                    {
                        type =
                            "boolean"
                    },

                    ["include_audit"] = new
                    {
                        type =
                            "boolean"
                    }
                };

            if (includePaymentStatus)
            {
                properties["payment_status"] =
                    new
                    {
                        type =
                            "string",

                        @enum =
                            new[]
                            {
                                "all",
                                "paid",
                                "unpaid"
                            }
                    };
            }

            return new
            {
                type =
                    "object",

                properties,

                additionalProperties =
                    false
            };
        }

        private static object GroupPeriodSchema(
            int[] gradeIds,
            bool includePaymentStatus = false)
        {
            var properties =
                new Dictionary<string, object>
                {
                    ["grade_id"] = new
                    {
                        type =
                            "integer",

                        @enum =
                            gradeIds
                    },

                    ["from_date"] = new
                    {
                        type =
                            "string",

                        description =
                            "YYYY-MM-DD"
                    },

                    ["to_date"] = new
                    {
                        type =
                            "string",

                        description =
                            "YYYY-MM-DD"
                    },

                    ["all_time"] = new
                    {
                        type =
                            "boolean"
                    },

                    ["include_audit"] = new
                    {
                        type =
                            "boolean"
                    }
                };

            if (includePaymentStatus)
            {
                properties["payment_status"] =
                    new
                    {
                        type =
                            "string",

                        @enum =
                            new[]
                            {
                                "all",
                                "paid",
                                "unpaid"
                            }
                    };
            }

            // grade_id intentionally NOT required.
            // لو المستخدم لم يحدد المرحلة،
            // Backend يسأله بدلاً من اختراع مرحلة.
            return new
            {
                type =
                    "object",

                properties,

                additionalProperties =
                    false
            };
        }

        // =========================================================
        // SYSTEM PROMPT
        // =========================================================

        private static string BuildSystemPrompt(
            ChatSessionState session,
            string gradesText,
            string rolesText,
            DateTime now)
        {
            var selectedPerson =
                session.SelectedPerson == null
                    ? "لا يوجد"
                    : $"{session.SelectedPerson.Name} | QR={session.SelectedPerson.Qr}";

            var previousMonth =
                now.AddMonths(-1);

            return
                $"""
                أنت مساعد ذكي لنظام إدارة خدمة كنسية.

                دورك الأساسي:
                فهم رسالة المستخدم واختيار Tool المناسبة فقط.

                البيانات الحقيقية يتم قراءتها بواسطة Backend.
                لا تخترع أي بيانات.

                ========================================
                DATE
                ========================================

                اليوم:
                {now:yyyy-MM-dd}

                الشهر الحالي:
                {now.Month}/{now.Year}

                الشهر السابق:
                {previousMonth.Month}/{previousMonth.Year}

                ========================================
                SELECTED PERSON
                ========================================

                الشخص المحدد حالياً:
                {selectedPerson}

                لو المستخدم يقول:
                هو
                ده
                الشخص ده
                حضوره
                غيابه
                اشتراكاته
                زياراته
                بياناته

                استخدم selected-person tool.

                لو ذكر اسم شخص جديد صراحة:
                استخدم tool بالاسم.

                ========================================
                PERSON VS GROUP
                ========================================

                قاعدة حاسمة:
                إذا احتوت الرسالة على اسم شخص مع طلب حضور أو غياب أو اشتراكات أو زيارات، فهذا طلب لشخص واحد. ابحث عن الاسم ونفذ الطلب، ولا تطلب المرحلة.
                عبارات مثل "غيابه" و"حضوره" و"اشتراكاته" تعود للشخص المذكور في نفس الرسالة أو المحدد في السياق.
                لا تعتبر كلمة "مرحلة" وحدها طلباً لمجموعة؛ قد يسأل المستخدم عن مرحلة الشخص.
                طلب المجموعة يكون واضحاً من كلمات مثل "الطلاب" أو "كل أفراد" أو اسم مرحلة، أو من طلب عام لا يحتوي اسم شخص.
                عند سؤال المستخدم عن اشتراكات مرحلة حتى شهر معين، نفذ تقرير المرحلة كاملاً حتى ذلك الشهر، مع الأشهر المدفوعة وغير المدفوعة لكل فرد.

                مثال Person:
                "هات غياب مينا ماجد الشهر اللي فات"
                => get_person_absence_by_name

                مثال Group:
                "هات الناس اللي غابت الشهر اللي فات"
                => get_group_absence

                مثال:
                "هات اللي مدفعوش الاشتراك الشهر ده"
                => get_group_subscriptions
                payment_status=unpaid

                مثال:
                "هات الزيارات من 1/9/2026 لحد 30/9/2026"
                => get_group_visitations

                مهم جداً:
                لو الطلب عن مجموعة ولم يحدد المستخدم مرحلة:
                لا تخترع grade_id.
                اترك grade_id بدون قيمة.
                Backend سيطلب من المستخدم تحديد المرحلة.

                ========================================
                PERIODS
                ========================================

                من 1/9/2026 لحد 30/9/2026
                => from_date / to_date

                من 1/9/2026 لحد دلوقتي
                => from_date=2026-09-01
                   to_date={now:yyyy-MM-dd}

                الشهر ده
                => الشهر الحالي.

                الشهر اللي فات
                => الشهر السابق.

                كل المدة
                => all_time=true.

                ========================================
                ATTENDANCE
                ========================================

                status=1 = حضور.
                status=0 = غياب.

                comment = تعليق.
                excused = عذر مرتبط بالسجل إن وجد.
                userName = الحساب الذي سجل الحالة.
                lastUpdated = وقت التسجيل أو آخر تحديث.

                ========================================
                SUBSCRIPTIONS
                ========================================

                isPaid=true = تم الدفع.
                isPaid=false = لم يتم الدفع.

                لو المستخدم يقول:
                مدفعش / مش دافع / غير مدفوع
                => payment_status=unpaid

                لو يقول:
                دفع / مدفوع
                => payment_status=paid

                ========================================
                VISITATIONS
                ========================================

                date = تاريخ الزيارة.
                comment = ملاحظة الزيارة.
                visitedBy = الحساب الذي سجل/قام بالزيارة.

                ========================================
                CURRENT GRADES
                ========================================

                {gradesText}

                المراحل ديناميكية.
                استخدم القائمة الحالية فقط.

                ========================================
                SERVANT ROLES
                ========================================

                {rolesText}

                roleId هو دور الخادم الثابت.

                ========================================
                SECURITY
                ========================================

                النظام READ ONLY.

                ممنوع:
                Add
                Edit
                Delete

                لا تخترع:
                اسم
                QR
                Grade
                Attendance
                Subscription
                Visitation

                الصلاحيات يطبقها JWT في Backend.

                تحدث بالعربية.
                """;
        }

        // =========================================================
        // TOOL CLASSIFICATION
        // =========================================================

        private static bool IsPersonByNameTool(
            string? name)
        {
            return name is
                "get_person_details_by_name"
                or
                "get_person_attendance_by_name"
                or
                "get_person_absence_by_name"
                or
                "get_person_subscriptions_by_name"
                or
                "get_person_visitations_by_name";
        }

        private static bool IsSelectedPersonTool(
            string? name)
        {
            return name is
                "get_selected_person_summary"
                or
                "get_selected_person_details"
                or
                "get_selected_person_attendance"
                or
                "get_selected_person_absence"
                or
                "get_selected_person_subscriptions"
                or
                "get_selected_person_visitations";
        }

        private static bool IsGroupTool(
            string? name)
        {
            return name is
                "get_group_attendance"
                or
                "get_group_absence"
                or
                "get_group_subscriptions"
                or
                "get_group_visitations";
        }

        // =========================================================
        // SELECTED PERSON
        // =========================================================

        private IActionResult SelectedPersonSummary(
            ChatSessionState session,
            string conversationId,
            List<GradeResult> grades)
        {
            var person =
                session.SelectedPerson;

            if (person == null)
            {
                return Ok(new
                {
                    conversationId,

                    type =
                        "person_required",

                    answer =
                        "حدد الشخص المقصود أولاً."
                });
            }

            var gradeName =
                person.Grade.HasValue
                    ? grades
                        .FirstOrDefault(
                            x =>
                                x.Id ==
                                person.Grade.Value
                        )
                        ?.Name
                    : null;

            return Ok(new
            {
                conversationId,

                type =
                    "person_selected",

                answer =
                    $"الشخص المحدد: {person.Name}"
                    +
                    (
                        !string.IsNullOrWhiteSpace(
                            gradeName
                        )
                            ? $" — {gradeName}"
                            : ""
                    ),

                selectedPerson =
                    ToBasicPerson(
                        person
                    ),

                actions =
                    SelectedPersonActions()
            });
        }

        private static object[]
            SelectedPersonActions()
        {
            return new object[]
            {
                new
                {
                    action =
                        "quick_reply",

                    label =
                        "البيانات",

                    message =
                        "هات بياناته"
                },

                new
                {
                    action =
                        "quick_reply",

                    label =
                        "غياب الشهر ده",

                    message =
                        "هات غيابه الشهر ده"
                },

                new
                {
                    action =
                        "quick_reply",

                    label =
                        "الاشتراكات",

                    message =
                        "هات اشتراكاته"
                }
            };
        }

        private static void SetSelectedPerson(
            ChatSessionState session,
            PersonResult person)
        {
            session.SelectedPerson =
                person;

            session.PendingCandidates.Clear();

            TouchSession(session);
        }

        // =========================================================
        // PENDING PERSON SELECTION
        // =========================================================

        private static PersonResult?
            ResolvePendingSelection(
                string text,
                List<PersonResult> candidates)
        {
            var normalized =
                NormalizeArabic(
                    text
                );

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(
                        candidate.Name)
                    &&
                    NormalizeArabic(
                        candidate.Name
                    )
                    ==
                    normalized)
                {
                    return candidate;
                }

                if (!string.IsNullOrWhiteSpace(
                        candidate.Qr)
                    &&
                    NormalizeArabic(
                        candidate.Qr
                    )
                    ==
                    normalized)
                {
                    return candidate;
                }
            }

            if (TryParseChoiceNumber(
                    normalized,
                    out var number)
                &&
                number >= 1
                &&
                number <= candidates.Count)
            {
                return candidates[
                    number - 1
                ];
            }

            return null;
        }

        private static bool TryParseChoiceNumber(
            string value,
            out int number)
        {
            number =
                0;

            var text =
                ConvertArabicDigits(
                    value
                )
                .Replace(
                    "رقم",
                    ""
                )
                .Replace(
                    "اختيار",
                    ""
                )
                .Trim();

            if (int.TryParse(
                    text,
                    out number))
            {
                return true;
            }

            var map =
                new Dictionary<string, int>
                {
                    { "الاول", 1 },
                    { "اول", 1 },
                    { "الاولى", 1 },

                    { "الثاني", 2 },
                    { "التاني", 2 },
                    { "ثاني", 2 },
                    { "تاني", 2 },

                    { "الثالث", 3 },
                    { "التالت", 3 },
                    { "ثالث", 3 },
                    { "تالت", 3 },

                    { "الرابع", 4 },
                    { "رابع", 4 },

                    { "الخامس", 5 },
                    { "خامس", 5 },

                    { "السادس", 6 },
                    { "سادس", 6 },

                    { "السابع", 7 },
                    { "سابع", 7 },

                    { "الثامن", 8 },
                    { "ثامن", 8 },

                    { "التاسع", 9 },
                    { "تاسع", 9 },

                    { "العاشر", 10 },
                    { "عاشر", 10 }
                };

            return map.TryGetValue(
                text,
                out number
            );
        }

        // =========================================================
        // GROUP / PERSON OUTPUT
        // =========================================================

        private static object ToAttendanceOutput(
            AttendanceRecord record)
        {
            return new
            {
                date =
                    record.Date,

                status =
                    record.Status,

                statusText =
                    record.Status switch
                    {
                        1 =>
                            "حضور",

                        0 =>
                            "غياب",

                        _ =>
                            "غير معروف"
                    },

                comment =
                    record.Comment,

                excused =
                    record.Excused,

                userName =
                    record.UserName,

                lastUpdated =
                    record.LastUpdated
            };
        }

        private static object ToBasicPerson(
            PersonResult person)
        {
            return new
            {
                id =
                    person.Id,

                qr =
                    person.Qr,

                name =
                    person.Name,

                grade =
                    person.Grade
            };
        }

        private static object ToBasicPersonWithRole(
            PersonResult person)
        {
            string? servantRole =
                null;

            if (person.RoleId.HasValue)
            {
                ServantsRolesStatic.TryGetValue(
                    person.RoleId.Value,
                    out servantRole
                );
            }

            return new
            {
                id =
                    person.Id,

                qr =
                    person.Qr,

                name =
                    person.Name,

                role =
                    person.Role,

                roleId =
                    person.RoleId,

                servantRole
            };
        }

        // =========================================================
        // SEARCH PEOPLE
        // =========================================================

        private static List<PersonResult>
            FilterPeople(
                List<PersonResult> people,
                string field,
                string mode,
                string value)
        {
            return people
                .Where(
                    person =>
                    {
                        var fieldValue =
                            GetPersonFieldValue(
                                person,
                                field
                            );

                        return mode switch
                        {
                            "has_value" =>
                                !string.IsNullOrWhiteSpace(
                                    fieldValue
                                ),

                            "is_empty" =>
                                string.IsNullOrWhiteSpace(
                                    fieldValue
                                ),

                            "equals" =>
                                NormalizeArabic(
                                    fieldValue
                                )
                                ==
                                NormalizeArabic(
                                    value
                                ),

                            "contains" =>
                                NormalizeArabic(
                                    fieldValue
                                )
                                .Contains(
                                    NormalizeArabic(
                                        value
                                    )
                                ),

                            _ =>
                                false
                        };
                    }
                )
                .ToList();
        }

        private static string?
            GetPersonFieldValue(
                PersonResult person,
                string field)
        {
            return field switch
            {
                "name" =>
                    person.Name,

                "nameEnglish" =>
                    person.NameEnglish,

                "phone" =>
                    person.Phone,

                "anotherPhone" =>
                    person.AnotherPhone,

                "address" =>
                    person.Address,

                "area" =>
                    person.Area,

                "location" =>
                    person.Location,

                "notes" =>
                    person.Notes,

                "excused" =>
                    person.Excused,

                "role" =>
                    person.Role,

                "details" =>
                    person.Details,

                "roleId" =>
                    person.RoleId?.ToString(),

                "dateOfBirth" =>
                    person.DateOfBirth,

                "gender" =>
                    person.Gender?.ToString(),

                "confessor" =>
                    person.Confessor,

                "createdAt" =>
                    person.CreatedAt,

                "createdBy" =>
                    person.CreatedBy,

                "updatedBy" =>
                    person.UpdatedBy,

                _ =>
                    null
            };
        }

        // =========================================================
        // GENERAL ANSWERS
        // =========================================================

        private static string BuildGradesAnswer(
            List<GradeResult> grades)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"المراحل الحالية: {grades.Count}"
            );

            builder.AppendLine();

            for (var i = 0; i < grades.Count; i++)
            {
                builder.AppendLine(
                    $"• {grades[i].Name}"
                );
            }

            return builder.ToString();
        }

        private static string BuildPeopleAnswer(
            GradeResult grade,
            List<PersonResult> people)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"{grade.Name} — العدد: {people.Count}"
            );

            builder.AppendLine();

            var display =
                people
                    .Take(60)
                    .ToList();

            for (var i = 0; i < display.Count; i++)
            {
                builder.AppendLine(
                    $"• {display[i].Name}"
                );
            }

            if (people.Count >
                display.Count)
            {
                builder.AppendLine();

                builder.AppendLine(
                    $"وهناك {people.Count - display.Count} فرد إضافي في البيانات."
                );
            }

            return builder.ToString();
        }

        private static string BuildSearchAnswer(
            GradeResult grade,
            string field,
            List<PersonResult> people)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"{grade.Name} — عدد النتائج: {people.Count}"
            );

            builder.AppendLine();

            foreach (
                var person
                in people.Take(50))
            {
                var value =
                    GetPersonFieldValue(
                        person,
                        field
                    );

                builder.AppendLine(
                    $"{person.Name}"
                    +
                    (
                        string.IsNullOrWhiteSpace(
                            value
                        )
                            ? ""
                            : $" — {value}"
                    )
                );
            }

            return builder.ToString();
        }

        private static string BuildServantRolesAnswer()
        {
            return string.Join(
                "\n",
                ServantsRolesStatic
                    .Select(
                        x =>
                            $"{x.Value} | ID: {x.Key}"
                    )
            );
        }

        private static string BuildServantsByRoleAnswer(
            string roleName,
            List<PersonResult> people)
        {
            var builder =
                new StringBuilder();

            builder.AppendLine(
                $"{roleName} — العدد: {people.Count}"
            );

            builder.AppendLine();

            for (var i = 0; i < people.Count; i++)
            {
                builder.AppendLine(
                    $"{i + 1}. {people[i].Name}"
                );
            }

            return builder.ToString();
        }

        // =========================================================
        // GRADE RESOLUTION
        // =========================================================

        private static bool TryResolveGradeFromMessage(
            string message,
            List<GradeResult> grades,
            out int gradeId)
        {
            gradeId =
                0;

            var normalized =
                NormalizeArabic(
                    ConvertArabicDigits(message)
                );

            var exactMatches =
                grades
                    .Where(
                        grade =>
                            NormalizeArabic(
                                ConvertArabicDigits(grade.Name)
                            ) == normalized
                    )
                    .ToList();

            if (exactMatches.Count == 1)
            {
                gradeId = exactMatches[0].Id;
                return true;
            }

            // Match a complete grade name inside the sentence, but
            // never choose when more than one current grade matches.
            var nameMatches =
                grades
                    .Where(
                        grade =>
                        {
                            var gradeName = NormalizeArabic(
                                ConvertArabicDigits(grade.Name)
                            );

                            return gradeName.Length > 1 &&
                                normalized.Contains(
                                    gradeName,
                                    StringComparison.Ordinal
                                );
                        }
                    )
                    .ToList();

            if (nameMatches.Count == 1)
            {
                gradeId = nameMatches[0].Id;
                return true;
            }

            // Support natural names such as "اعدادي" when the
            // database contains one matching grade named "مرحلة اعدادي".
            var aliases = new[]
            {
                "ابتدائي",
                "اعدادي",
                "ثانوي",
                "جامعه",
                "خريجين",
                "خدام",
                "خادم",
                "خدامين"
            };

            foreach (var alias in aliases)
            {
                var normalizedAlias = NormalizeArabic(alias);

                if (!normalized.Contains(normalizedAlias))
                {
                    continue;
                }

                var aliasMatches =
                    grades
                        .Where(
                            grade => NormalizeArabic(
                                ConvertArabicDigits(grade.Name)
                            ).Contains(
                                normalizedAlias,
                                StringComparison.Ordinal
                            )
                        )
                        .ToList();

                if (aliasMatches.Count == 1)
                {
                    gradeId = aliasMatches[0].Id;
                    return true;
                }
            }

            return false;
        }

        private static GradeResult?
            FindServantsGrade(
                List<GradeResult> grades)
        {
            return grades.FirstOrDefault(
                       x =>
                           NormalizeArabic(
                               x.Name
                           )
                           ==
                           NormalizeArabic(
                               "الخدام"
                           )
                   )
                   ??
                   grades.FirstOrDefault(
                       x =>
                           NormalizeArabic(
                               x.Name
                           )
                           .Contains(
                               "خدام"
                           )
                   );
        }

        // =========================================================
        // PAYMENT STATUS
        // =========================================================

        private static string DetectPaymentStatus(
            string message)
        {
            var normalized =
                NormalizeArabic(
                    message
                );

            if (ContainsAny(
                    normalized,
                    "مدفعش",
                    "مادفعش",
                    "ما دفعش",
                    "ما دفعوش",
                    "مدفعوش",
                    "مش دافع",
                    "مش مدفوع",
                    "غير مدفوع",
                    "غير مدفوعين",
                    "لم يدفع",
                    "لم يدفعوا",
                    "اللي مدفعوش"))
            {
                return "unpaid";
            }

            if (ContainsAny(
                    normalized,
                    "اللي دفعوا",
                    "دفع الاشتراك",
                    "دافع",
                    "مدفوع",
                    "مدفوعين"))
            {
                return "paid";
            }

            return "all";
        }

        private static string ResolvePaymentStatus(
            string? toolValue,
            string message)
        {
            var detected = DetectPaymentStatus(message);
            var normalizedToolValue = NormalizePaymentStatus(toolValue);

            // Some model responses send payment_status=all even when
            // the Arabic message explicitly asks for paid/unpaid
            // records.  Natural-language intent has priority over the
            // model default, while an explicit paid/unpaid value is
            // still respected.
            if (detected != "all" && normalizedToolValue == "all")
            {
                return detected;
            }

            return normalizedToolValue;
        }

        private static string NormalizePaymentStatus(
            string? value)
        {
            return value?.ToLowerInvariant() switch
            {
                "paid" =>
                    "paid",

                "unpaid" =>
                    "unpaid",

                _ =>
                    "all"
            };
        }

        // =========================================================
        // SESSION HELPERS
        // =========================================================

        private string BuildSessionKey(
            string conversationId,
            string authorization)
        {
            var userId =
                User.FindFirst(
                    ClaimTypes.NameIdentifier
                )?.Value
                ??
                User.FindFirst(
                    "sub"
                )?.Value
                ??
                User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(
                userId))
            {
                using var sha =
                    SHA256.Create();

                userId =
                    Convert.ToHexString(
                        sha.ComputeHash(
                            Encoding.UTF8.GetBytes(
                                authorization
                            )
                        )
                    );
            }

            return
                $"{userId}:{conversationId}";
        }

        private static string GetOrCreateConversationId(
            string? conversationId)
        {
            if (!string.IsNullOrWhiteSpace(
                    conversationId))
            {
                var trimmed =
                    conversationId.Trim();

                if (Regex.IsMatch(
                        trimmed,
                        @"^[A-Za-z0-9_-]{1,80}$"
                    ))
                {
                    return trimmed;
                }
            }

            return Guid
                .NewGuid()
                .ToString("N");
        }

        private static void TouchSession(
            ChatSessionState session)
        {
            session.UpdatedAtUtc =
                DateTime.UtcNow;
        }

        private static void CleanupOldSessions()
        {
            var now =
                DateTime.UtcNow;

            foreach (var item in ChatSessions)
            {
                if (
                    now -
                    item.Value.UpdatedAtUtc
                    >
                    SessionLifetime)
                {
                    ChatSessions.TryRemove(
                        item.Key,
                        out _
                    );
                }
            }
        }

        private static PersonAction ClonePersonAction(
            PersonAction action)
        {
            return new PersonAction
            {
                Kind =
                    action.Kind,

                FromDate =
                    action.FromDate,

                ToDate =
                    action.ToDate,

                AllTime =
                    action.AllTime,

                IncludeAudit =
                    action.IncludeAudit,

                PaymentStatus =
                    action.PaymentStatus
            };
        }

        private static GroupAction CloneGroupAction(
            GroupAction action)
        {
            return new GroupAction
            {
                Kind =
                    action.Kind,

                GradeId =
                    action.GradeId,

                FromDate =
                    action.FromDate,

                ToDate =
                    action.ToDate,

                AllTime =
                    action.AllTime,

                IncludeAudit =
                    action.IncludeAudit,

                PaymentStatus =
                    action.PaymentStatus
            };
        }

        // =========================================================
        // JSON HELPERS
        // =========================================================

        private static bool TryGetArguments(
            JsonElement function,
            out JsonElement args)
        {
            args =
                default;

            if (!function.TryGetProperty(
                    "arguments",
                    out var arguments))
            {
                return false;
            }

            try
            {
                using var document =
                    JsonDocument.Parse(
                        arguments.GetString()
                        ??
                        "{}"
                    );

                args =
                    document
                        .RootElement
                        .Clone();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetInt(
            JsonElement args,
            string property,
            out int value)
        {
            value =
                0;

            return args.ValueKind ==
                    JsonValueKind.Object
                &&
                args.TryGetProperty(
                    property,
                    out var element
                )
                &&
                element.ValueKind ==
                    JsonValueKind.Number
                &&
                element.TryGetInt32(
                    out value
                );
        }

        private static bool TryGetString(
            JsonElement args,
            string property,
            out string? value)
        {
            value =
                null;

            if (args.ValueKind !=
                    JsonValueKind.Object
                ||
                !args.TryGetProperty(
                    property,
                    out var element
                )
                ||
                element.ValueKind !=
                    JsonValueKind.String)
            {
                return false;
            }

            value =
                element.GetString();

            return !string.IsNullOrWhiteSpace(
                value
            );
        }

        private static bool TryGetBool(
            JsonElement args,
            string property,
            out bool value)
        {
            value =
                false;

            if (args.ValueKind !=
                    JsonValueKind.Object
                ||
                !args.TryGetProperty(
                    property,
                    out var element
                ))
            {
                return false;
            }

            if (element.ValueKind !=
                    JsonValueKind.True
                &&
                element.ValueKind !=
                    JsonValueKind.False)
            {
                return false;
            }

            value =
                element.GetBoolean();

            return true;
        }

        private static int? GetNullableInt(
            JsonElement item,
            string property)
        {
            if (item.TryGetProperty(
                    property,
                    out var element)
                &&
                element.ValueKind ==
                    JsonValueKind.Number
                &&
                element.TryGetInt32(
                    out var value))
            {
                return value;
            }

            return null;
        }

        private static string? GetNullableString(
            JsonElement item,
            string property)
        {
            if (item.TryGetProperty(
                    property,
                    out var element)
                &&
                element.ValueKind ==
                    JsonValueKind.String)
            {
                return element.GetString();
            }

            return null;
        }

        private static bool? GetNullableBool(
            JsonElement item,
            string property)
        {
            if (!item.TryGetProperty(
                    property,
                    out var element))
            {
                return null;
            }

            if (element.ValueKind ==
                JsonValueKind.True)
            {
                return true;
            }

            if (element.ValueKind ==
                JsonValueKind.False)
            {
                return false;
            }

            return null;
        }

        private static DateTime? GetNullableDate(
            JsonElement item,
            string property)
        {
            if (!item.TryGetProperty(
                    property,
                    out var element)
                ||
                element.ValueKind !=
                    JsonValueKind.String)
            {
                return null;
            }

            return DateTime.TryParse(
                    element.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                ? date
                : null;
        }

        private static bool IsJsonArray(
            string raw)
        {
            try
            {
                using var document =
                    JsonDocument.Parse(
                        raw
                    );

                return document.RootElement.ValueKind ==
                    JsonValueKind.Array;
            }
            catch
            {
                return false;
            }
        }

        // =========================================================
        // DATE HELPERS
        // =========================================================

        private static bool TryParseFlexibleDate(
            string value,
            out DateTime date)
        {
            value =
                ConvertArabicDigits(
                    value
                );

            var formats =
                new[]
                {
                    "yyyy-MM-dd",
                    "yyyy/M/d",
                    "yyyy-MM-d",
                    "yyyy-M-dd",
                    "d/M/yyyy",
                    "dd/MM/yyyy",
                    "d-M-yyyy",
                    "dd-MM-yyyy",
                    "M/d/yyyy",
                    "MM/dd/yyyy"
                };

            return DateTime.TryParseExact(
                    value,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date
                )
                ||
                DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date
                );
        }

        private static int? ExtractYearFromMessage(
            string value)
        {
            var match =
                Regex.Match(
                    value,
                    @"\b(20\d{2})\b"
                );

            if (match.Success &&
                int.TryParse(
                    match.Groups[1].Value,
                    out var year))
            {
                return year;
            }

            return null;
        }

        private static DateTime GetEgyptNow()
        {
            try
            {
                var timezone =
                    TimeZoneInfo.FindSystemTimeZoneById(
                        "Egypt Standard Time"
                    );

                return TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    timezone
                );
            }
            catch
            {
                try
                {
                    var timezone =
                        TimeZoneInfo.FindSystemTimeZoneById(
                            "Africa/Cairo"
                        );

                    return TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.UtcNow,
                        timezone
                    );
                }
                catch
                {
                    return DateTime.UtcNow;
                }
            }
        }

        private static string FormatDate(
            DateTime? date)
        {
            return date.HasValue
                ? date.Value.ToString(
                    "yyyy-MM-dd"
                )
                : "تاريخ غير معروف";
        }

        private static string FormatDateTime(
            DateTime? date)
        {
            return date.HasValue
                ? date.Value.ToString(
                    "yyyy-MM-dd HH:mm"
                )
                : "غير معروف";
        }

        // =========================================================
        // ARABIC HELPERS
        // =========================================================

        private static string NormalizeArabic(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                value))
            {
                return "";
            }

            var result =
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Replace('أ', 'ا')
                    .Replace('إ', 'ا')
                    .Replace('آ', 'ا')
                    .Replace('ٱ', 'ا')
                    .Replace('ى', 'ي')
                    .Replace('ؤ', 'و')
                    .Replace('ئ', 'ي');

            result =
                Regex.Replace(
                    result,
                    @"[\u064B-\u065F\u0670]",
                    ""
                );

            return Regex.Replace(
                result,
                @"\s+",
                " "
            );
        }

        private static string ConvertArabicDigits(
            string value)
        {
            return value
                .Replace('٠', '0')
                .Replace('١', '1')
                .Replace('٢', '2')
                .Replace('٣', '3')
                .Replace('٤', '4')
                .Replace('٥', '5')
                .Replace('٦', '6')
                .Replace('٧', '7')
                .Replace('٨', '8')
                .Replace('٩', '9');
        }

        private static bool ContainsAny(
            string source,
            params string[] values)
        {
            foreach (var value in values)
            {
                if (source.Contains(
                    NormalizeArabic(
                        value
                    )))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAuditIntent(
            string source)
        {
            return ContainsAny(
                source,
                "مين سجل",
                "مين اللي سجل",
                "سجلها مين",
                "اتسجل بواسطه مين",
                "اتسجل بواسطة مين",
                "مين عمل التسجيل",
                "مين سجل الغياب",
                "مين سجل الحضور",
                "امتى اتسجل",
                "امتي اتسجل",
                "تاريخ التسجيل",
                "اخر تحديث",
                "مين زاره",
                "مين عمل الزياره",
                "مين عمل الزيارة"
            );
        }

        private static bool LooksLikeDataRequest(
            string message)
        {
            var normalized =
                NormalizeArabic(
                    message
                );

            return ContainsAny(
                normalized,
                "هات",
                "اعرض",
                "جيب",
                "بيانات",
                "تفاصيل",
                "طلاب",
                "خدام",
                "مرحله",
                "صف",
                "حضور",
                "غياب",
                "غاب",
                "اشتراك",
                "دفع",
                "زياره",
                "زيارات",
                "كنيسه",
                "خدمه",
                "رقم",
                "عنوان",
                "اب اعتراف",
                "ملاحظات",
                "عذر",
                "وظيفه",
                "كام",
                "عدد",
                "مين سجل",
                "مين زار"
            );
        }

        // =========================================================
        // FORMATTING
        // =========================================================

        private static string SafeText(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                value)
                ||
                value.Trim() ==
                "—")
            {
                return "غير معروف";
            }

            return value;
        }

        private IActionResult RangeTooLarge(
            string conversationId)
        {
            return Ok(new
            {
                conversationId,

                type =
                    "range_too_large",

                answer =
                    "الفترة كبيرة جداً لهذا النوع من البيانات. حدد فترة أقل من 36 شهر علشان أقدر أجيب النتيجة بدقة ومن غير ما أثقل على النظام."
            });
        }

        // =========================================================
        // HTTP TO EXISTING BACKEND APIS
        // =========================================================

        private HttpClient CreateAuthorizedClient(
            string authorization)
        {
            var client =
                _httpClientFactory
                    .CreateClient();

            client.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(
                    authorization
                );

            return client;
        }

        private string BuildLocalUrl(
            string path)
        {
            return
                $"{Request.Scheme}://{Request.Host}{path}";
        }

        private async Task<ExactApiResult>
            GetExactLocal(
                string path,
                string authorization)
        {
            var client =
                CreateAuthorizedClient(
                    authorization
                );

            var response =
                await client.GetAsync(
                    BuildLocalUrl(
                        path
                    )
                );

            return new ExactApiResult
            {
                Success =
                    response.IsSuccessStatusCode,

                StatusCode =
                    (int)response.StatusCode,

                Raw =
                    await response.Content
                        .ReadAsStringAsync()
            };
        }

        private async Task<ExactApiResult>
            PostExactLocal(
                string path,
                string authorization,
                object body)
        {
            var client =
                CreateAuthorizedClient(
                    authorization
                );

            var json =
                JsonSerializer.Serialize(
                    body
                );

            var content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                );

            var response =
                await client.PostAsync(
                    BuildLocalUrl(
                        path
                    ),
                    content
                );

            return new ExactApiResult
            {
                Success =
                    response.IsSuccessStatusCode,

                StatusCode =
                    (int)response.StatusCode,

                Raw =
                    await response.Content
                        .ReadAsStringAsync()
            };
        }

        private IActionResult ExactApiResponse(
            string type,
            string title,
            ExactApiResult api,
            string conversationId,
            PersonResult? selectedPerson = null)
        {
            if (!api.Success)
            {
                return StatusCode(
                    api.StatusCode,
                    new
                    {
                        conversationId,

                        message =
                            "حدث خطأ أثناء جلب البيانات.",

                        details =
                            api.Raw
                    }
                );
            }

            object parsed;

            try
            {
                using var document =
                    JsonDocument.Parse(
                        api.Raw
                    );

                parsed =
                    document
                        .RootElement
                        .Clone();
            }
            catch
            {
                parsed =
                    api.Raw;
            }

            return Ok(new
            {
                conversationId,

                type,

                answer =
                    title,

                data =
                    parsed,

                selectedPerson =
                    selectedPerson == null
                        ? null
                        : ToBasicPerson(
                            selectedPerson
                        )
            });
        }

        private IActionResult ApiError(
            PeopleResult result,
            string conversationId)
        {
            return StatusCode(
                result.StatusCode,
                new
                {
                    conversationId,

                    message =
                        result.ErrorMessage
                }
            );
        }

        // =========================================================
        // GROQ
        // =========================================================

        private async Task<GroqResult>
            SendToGroq(
                string apiKey,
                List<object> messages,
                object[] tools,
                string toolChoice)
        {
            var client =
                _httpClientFactory
                    .CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    apiKey
                );

            var payload = new
            {
                model =
                    "openai/gpt-oss-20b",

                include_reasoning =
                    false,

                temperature =
                    0,

                messages,

                tools,

                tool_choice =
                    toolChoice
            };

            var json =
                JsonSerializer.Serialize(
                    payload
                );

            var content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                );

            var response =
                await client.PostAsync(
                    "https://api.groq.com/openai/v1/chat/completions",
                    content
                );

            return new GroqResult
            {
                Success =
                    response.IsSuccessStatusCode,

                StatusCode =
                    (int)response.StatusCode,

                Raw =
                    await response.Content
                        .ReadAsStringAsync()
            };
        }

        // =========================================================
        // MISC
        // =========================================================

        private static List<PersonResult>
            FilterPeopleDummy(
                List<PersonResult> people)
        {
            return people;
        }

        // =========================================================
        // INTERNAL RESULT CLASSES
        // =========================================================

        private class GroqResult
        {
            public bool Success { get; set; }

            public int StatusCode { get; set; }

            public string Raw { get; set; } = "";
        }

        private class ExactApiResult
        {
            public bool Success { get; set; }

            public int StatusCode { get; set; }

            public string Raw { get; set; } = "";
        }

        private class GradesResult
        {
            public bool Success { get; set; }

            public int StatusCode { get; set; }

            public string? ErrorMessage { get; set; }

            public List<GradeResult> Grades { get; set; } =
                new();
        }

        private class PeopleResult
        {
            public bool Success { get; set; }

            public int StatusCode { get; set; }

            public string? ErrorMessage { get; set; }

            public List<PersonResult> People { get; set; } =
                new();
        }
    }
}
