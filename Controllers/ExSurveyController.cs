using Microsoft.AspNetCore.Mvc;
using Survery_Certification_App_Consume.Models.Survey;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Health_Matrix_Consuming_Side.Controllers
{
    public class ExSurveyController : Controller
    {
        private readonly IHttpClientFactory _clientFactory;

        public ExSurveyController(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public async Task<IActionResult> Index()
        {
            var client = CreateApiClient();

            var jobs = await client.GetFromJsonAsync<List<SurveyJobListItemViewModel>>(
                "survey/jobs?moduleType=ExSurvey"
            ) ?? new List<SurveyJobListItemViewModel>();

            return View(jobs);
        }

        [HttpGet]
        public IActionResult CreateJob()
        {
            return View(new CreateSurveyJobViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> CreateJob(CreateSurveyJobViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var client = CreateApiClient();

            var payload = new
            {
                moduleType = 1, // ExSurvey 
                jobNumber = model.JobNumber,
                clientName = model.ClientName,
                projectName = model.ProjectName,
                vesselName = model.VesselName,
                siteName = model.SiteName,
                startDate = model.StartDate,
                endDate = model.EndDate,
                createdByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
            };

            var response = await client.PostAsJsonAsync("survey/jobs", payload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                ModelState.AddModelError("", error);
                return View(model);
            }

            TempData["SuccessMessage"] = "Ex-Survey job created successfully.";
            return RedirectToAction(nameof(Index));
        }

        private HttpClient CreateApiClient()
        {
            var client = _clientFactory.CreateClient("ApiClient");

            var jwt = HttpContext.Session.GetString("JWT");
            if (!string.IsNullOrEmpty(jwt))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", jwt);
            }

            return client;
        }

        public async Task<IActionResult> Details(int id)
        {
            var client = CreateApiClient();

            var job = await client.GetFromJsonAsync<SurveyJobDetailsViewModel>(
                $"survey/jobs/{id}"
            );

            if (job == null)
                return NotFound();

            var pageModel = new SurveyJobDetailsPageViewModel
            {
                Job = job,
                Assignment = new AssignSurveyorViewModel
                {
                    SurveyJobId = id
                }
            };

            return View(pageModel);
        }

        [HttpPost]
        public async Task<IActionResult> AssignSurveyor(AssignSurveyorViewModel model)
        {
            if (model.SurveyJobId <= 0 || model.SurveyorUserId <= 0)
            {
                TempData["ErrorMessage"] = "Please enter a valid surveyor user id.";
                return RedirectToAction(nameof(Details), new { id = model.SurveyJobId });
            }

            var client = CreateApiClient();

            var payload = new
            {
                surveyorUserId = model.SurveyorUserId,
                assignedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
            };

            var response = await client.PostAsJsonAsync(
                $"survey/jobs/{model.SurveyJobId}/assign",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(Details), new { id = model.SurveyJobId });
            }

            TempData["SuccessMessage"] = "Surveyor assigned successfully.";
            return RedirectToAction(nameof(Details), new { id = model.SurveyJobId });
        }

        public async Task<IActionResult> EquipmentRegister(int id)
        {
            var client = CreateApiClient();

            var equipment = await client.GetFromJsonAsync<List<ExEquipmentViewModel>>(
                $"ex-survey/jobs/{id}/equipment"
            ) ?? new List<ExEquipmentViewModel>();

            var model = new ExEquipmentRegisterPageViewModel
            {
                SurveyJobId = id,
                Equipment = equipment,
                NewEquipment = new CreateExEquipmentViewModel
                {
                    SurveyJobId = id
                }
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddEquipment(CreateExEquipmentViewModel model)
        {
            if (model.SurveyJobId <= 0 || string.IsNullOrWhiteSpace(model.EquipmentId))
            {
                TempData["ErrorMessage"] = "Equipment ID is required.";
                return RedirectToAction(nameof(EquipmentRegister), new { id = model.SurveyJobId });
            }

            var client = CreateApiClient();

            var payload = new
            {
                equipmentId = model.EquipmentId,
                rfidTag = model.RfidTag,
                area = model.Area,
                location = model.Location,
                equipmentType = model.EquipmentType,
                description = model.Description,
                manufacturer = model.Manufacturer,
                model = model.Model,
                serialNumber = model.SerialNumber
            };

            var response = await client.PostAsJsonAsync(
                $"ex-survey/jobs/{model.SurveyJobId}/equipment",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(EquipmentRegister), new { id = model.SurveyJobId });
            }

            TempData["SuccessMessage"] = "Equipment added successfully.";
            return RedirectToAction(nameof(EquipmentRegister), new { id = model.SurveyJobId });
        }

        public async Task<IActionResult> DefectRegister(int id)
        {
            var client = CreateApiClient();

            var defects = await client.GetFromJsonAsync<List<ExDefectViewModel>>(
                $"ex-survey/jobs/{id}/defects"
            ) ?? new List<ExDefectViewModel>();

            ViewBag.SurveyJobId = id;
            return View(defects);
        }

        public async Task<IActionResult> ChecklistQuestions(int id)
        {
            var client = CreateApiClient();

            var questions = await client.GetFromJsonAsync<List<ExChecklistQuestionViewModel>>(
                "ex-survey/checklist/questions"
            ) ?? new List<ExChecklistQuestionViewModel>();

            var model = new ExChecklistQuestionPageViewModel
            {
                SurveyJobId = id,
                Questions = questions,
                NewQuestion = new CreateExChecklistQuestionViewModel
                {
                    SurveyJobId = id
                }
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddChecklistQuestion(CreateExChecklistQuestionViewModel model)
        {
            if (model.SurveyJobId <= 0 ||
                string.IsNullOrWhiteSpace(model.QuestionCode) ||
                string.IsNullOrWhiteSpace(model.QuestionText))
            {
                TempData["ErrorMessage"] = "Question code and question text are required.";
                return RedirectToAction(nameof(ChecklistQuestions), new { id = model.SurveyJobId });
            }

            var client = CreateApiClient();

            var payload = new
            {
                questionCode = model.QuestionCode,
                section = model.Section,
                questionText = model.QuestionText,
                sortOrder = model.SortOrder
            };

            var response = await client.PostAsJsonAsync(
                "ex-survey/checklist/questions",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(ChecklistQuestions), new { id = model.SurveyJobId });
            }

            TempData["SuccessMessage"] = "Checklist question added successfully.";
            return RedirectToAction(nameof(ChecklistQuestions), new { id = model.SurveyJobId });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int surveyJobId, string status)
        {
            if (surveyJobId <= 0 || string.IsNullOrWhiteSpace(status))
            {
                TempData["ErrorMessage"] = "Invalid survey status request.";
                return RedirectToAction(nameof(Index));
            }

            var client = CreateApiClient();

            var payload = new
            {
                status = status
            };

            var response = await client.PostAsJsonAsync(
                $"survey/jobs/{surveyJobId}/status",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(Details), new { id = surveyJobId });
            }

            TempData["SuccessMessage"] = $"Survey status updated to {status}.";
            return RedirectToAction(nameof(Details), new { id = surveyJobId });
        }
        public async Task<IActionResult> Photos(int id)
        {
            var client = CreateApiClient();

            var photos = await client.GetFromJsonAsync<List<SurveyPhotoViewModel>>(
                $"survey/jobs/{id}/photos"
            ) ?? new List<SurveyPhotoViewModel>();

            ViewBag.SurveyJobId = id;
            ViewBag.Title = "Ex-Survey Photos";

            return View("~/Views/Survey/Photos.cshtml", photos);
        }
        public async Task<IActionResult> GenerateReport(int id)
        {
            var client = CreateApiClient();

            var payload = new
            {
                status = "ReportGenerated"
            };

            var response = await client.PostAsJsonAsync(
                $"survey/jobs/{id}/status",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["SuccessMessage"] = "Ex-Survey report marked as generated.";
            return RedirectToAction(nameof(Details), new { id });
        }


    }
}
