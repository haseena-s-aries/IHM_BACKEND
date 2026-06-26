using Microsoft.AspNetCore.Mvc;
using Survery_Certification_App_Consume.Models.Survey;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Health_Matrix_Consuming_Side.Controllers
{
    public class IhmSurveyController : Controller
    {
        private readonly IHttpClientFactory _clientFactory;

        public IhmSurveyController(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public async Task<IActionResult> Index()
        {
            var client = CreateApiClient();

            var jobs = await client.GetFromJsonAsync<List<SurveyJobListItemViewModel>>(
                "survey/jobs?moduleType=IhmSurvey"
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
                moduleType = 2, // IhmSurvey 
                jobNumber = model.JobNumber,
                clientName = model.ClientName,
                projectName = model.ProjectName,
                vesselName = model.VesselName,
                imoNumber = model.ImoNumber,
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

            TempData["SuccessMessage"] = "IHM Survey job created successfully.";
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
        public async Task<IActionResult> Areas(int id)
        {
            var client = CreateApiClient();

            var areas = await client.GetFromJsonAsync<List<IhmAreaViewModel>>(
                $"ihm-survey/jobs/{id}/areas"
            ) ?? new List<IhmAreaViewModel>();

            var model = new IhmAreaPageViewModel
            {
                SurveyJobId = id,
                Areas = areas,
                NewArea = new CreateIhmAreaViewModel
                {
                    SurveyJobId = id
                }
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddArea(CreateIhmAreaViewModel model)
        {
            if (model.SurveyJobId <= 0 || string.IsNullOrWhiteSpace(model.AreaName))
            {
                TempData["ErrorMessage"] = "Area name is required.";
                return RedirectToAction(nameof(Areas), new { id = model.SurveyJobId });
            }

            var client = CreateApiClient();

            var payload = new
            {
                areaName = model.AreaName,
                description = model.Description,
                sortOrder = model.SortOrder
            };

            var response = await client.PostAsJsonAsync(
                $"ihm-survey/jobs/{model.SurveyJobId}/areas",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(Areas), new { id = model.SurveyJobId });
            }

            TempData["SuccessMessage"] = "IHM area added successfully.";
            return RedirectToAction(nameof(Areas), new { id = model.SurveyJobId });
        }

        public async Task<IActionResult> Checkpoints(int areaId, int surveyJobId)
        {
            var client = CreateApiClient();

            var checkpoints = await client.GetFromJsonAsync<List<IhmCheckpointViewModel>>(
                $"ihm-survey/areas/{areaId}/checkpoints"
            ) ?? new List<IhmCheckpointViewModel>();

            var model = new IhmCheckpointPageViewModel
            {
                SurveyJobId = surveyJobId,
                IhmAreaId = areaId,
                Checkpoints = checkpoints,
                NewCheckpoint = new CreateIhmCheckpointViewModel
                {
                    SurveyJobId = surveyJobId,
                    IhmAreaId = areaId
                }
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddCheckpoint(CreateIhmCheckpointViewModel model)
        {
            if (model.IhmAreaId <= 0 ||
                model.SurveyJobId <= 0 ||
                string.IsNullOrWhiteSpace(model.CheckpointCode) ||
                string.IsNullOrWhiteSpace(model.Description))
            {
                TempData["ErrorMessage"] = "Checkpoint code and description are required.";
                return RedirectToAction(
                    nameof(Checkpoints),
                    new { areaId = model.IhmAreaId, surveyJobId = model.SurveyJobId }
                );
            }

            var client = CreateApiClient();

            var payload = new
            {
                checkpointCode = model.CheckpointCode,
                description = model.Description,
                materialCategory = model.MaterialCategory,
                hazardousMaterial = model.HazardousMaterial
            };

            var response = await client.PostAsJsonAsync(
                $"ihm-survey/areas/{model.IhmAreaId}/checkpoints",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(
                    nameof(Checkpoints),
                    new { areaId = model.IhmAreaId, surveyJobId = model.SurveyJobId }
                );
            }

            TempData["SuccessMessage"] = "IHM checkpoint added successfully.";
            return RedirectToAction(
                nameof(Checkpoints),
                new { areaId = model.IhmAreaId, surveyJobId = model.SurveyJobId }
            );
        }
        [HttpPost]
        public async Task<IActionResult> ImportExcel(int surveyJobId, IFormFile file)
        {
            if (surveyJobId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid survey job.";
                return RedirectToAction(nameof(Index));
            }

            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select an Excel file.";
                return RedirectToAction(nameof(Details), new { id = surveyJobId });
            }

            var client = CreateApiClient();

            using var content = new MultipartFormDataContent();

            using var fileStream = file.OpenReadStream();

            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

            content.Add(fileContent, "file", file.FileName);

            var response = await client.PostAsync(
                $"ihm-survey/jobs/{surveyJobId}/import-excel",
                content
            );

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
                return RedirectToAction(nameof(Details), new { id = surveyJobId });
            }

            TempData["SuccessMessage"] = "IHM Excel imported successfully.";
            return RedirectToAction(nameof(Areas), new { id = surveyJobId });
        }
        public async Task<IActionResult> Photos(int id)
        {
            var client = CreateApiClient();

            var photos = await client.GetFromJsonAsync<List<SurveyPhotoViewModel>>(
                $"survey/jobs/{id}/photos"
            ) ?? new List<SurveyPhotoViewModel>();

            ViewBag.SurveyJobId = id;
            ViewBag.Title = "IHM Survey Photos";

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

            TempData["SuccessMessage"] = "IHM Survey report marked as generated.";
            return RedirectToAction(nameof(Details), new { id });
        }


    }
}