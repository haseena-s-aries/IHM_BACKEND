using Survey_Certification_App.DTOS;

namespace Survey_Certification_App.Controllers
{
    internal class PagedResult<T>
    {
        public List<cerificatedto> Data { get; set; }
        public int TotalCount { get; set; }
    }
}