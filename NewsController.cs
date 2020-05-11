using Jevic.Common.Consts;
using Jevic.Common.Enums;
using Jevic.Core.Contracts.Models.Blob;
using Jevic.Core.Contracts.Models.DocumentExamination;
using Jevic.Core.Contracts.Models.News;
using Jevic.Core.Contracts.Services;
using Jevic.Web.Attributes;
using Jevic.Web.Extensions;
using Jevic.Web.Mappers;
using Jevic.Web.Models;
using Jevic.Web.ViewModels.News;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlobInfoModel = Jevic.Web.Models.BlobInfoModel;

namespace Jevic.Web.Controllers
{
    public class NewsController : AuthorizedController
    {
        private readonly INewsService _service;
        private readonly IViewModelMapper _mapper;
        private readonly IBlobFileService _fileService;

        public static string TEMP_NEWS_SEARCH_KEY = nameof(TEMP_NEWS_SEARCH_KEY);
        private const string TEMP_NEWS_DETAIL_KEY = nameof(TEMP_NEWS_DETAIL_KEY);
        public static string TEMP_NEWS_SUCCESS_KEY = nameof(TEMP_NEWS_SUCCESS_KEY);
        private const string IDENTITY_DEFAULT_NAME = "AnonymousErrorUser";

        public NewsController(INewsService service, IViewModelMapper mapper, IBlobFileService fileService)
        {
            _service = service;
            _mapper = mapper;
            _fileService = fileService;
        }

        [HttpGet]
        [AuthRequired(AuthorityId.NewsRefer)]
        public IActionResult List()
        {
            var vm = new NewsListViewModel();
            if (TempData.ContainsKey(TEMP_NEWS_SEARCH_KEY))
            {
                MergeToModelState(vm);
                var tempdata = TempData.Get<NewsSearchCondition>(TEMP_NEWS_SEARCH_KEY);
                var successMessage = TempData.Get<string>(TEMP_NEWS_SUCCESS_KEY);
                if (successMessage != null)
                {
                     vm = new NewsListViewModel { SearchCondition = tempdata, SuccessMessage = successMessage };
                   
                }
                else
                {
                     vm = new NewsListViewModel { SearchCondition = tempdata };
                    MergeToModelState(vm);
                }
            }
            TempData.Clear();
            return View(vm);
        }

        [HttpGet]
        [AuthRequired(AuthorityId.NewsRefer)]
        public async Task<IActionResult> Detail(string newsId)
        {
            var vm = new NewsDetailViewModel();

            //PostBack時
            if (TempData.ContainsKey(TEMP_NEWS_DETAIL_KEY))
            {
                vm = TempData.Get<NewsDetailViewModel>(TEMP_NEWS_DETAIL_KEY);
                MergeToModelState(vm);
                if (vm.PDFFileJson != null)
                {
                    BlobInfoModel blob = JsonConvert.DeserializeObject<BlobInfoModel>(vm.PDFFileJson);
                }

                if (vm.PDFFileEngJson != null)
                {
                    BlobInfoModel blob = JsonConvert.DeserializeObject<BlobInfoModel>(vm.PDFFileEngJson);
                }
                return View(vm);
            }

            //更新の場合
            if (!string.IsNullOrEmpty(newsId) && int.TryParse(newsId, out var id))
            {
                var condition = await _service.GetByNewsId(id);
                TempLastUpdatedAt = condition.LastUpdatedAt;
                vm = _mapper.MapToNewsDetailViewModel(condition);
                if (condition.NewsBlobId != null)
                {
                    var blobInfo = new BlobInfoModel { BlobId = condition.NewsBlobId, BlobName = condition.NewsBlobName, FileName = condition.NewsDocumentName };
                    string PDFJSON = JsonConvert.SerializeObject(blobInfo);
                    vm.PDFFileJson = PDFJSON;
                }

                if (condition.NewsBlobIdEng != null)
                {
                    var blobInfo = new BlobInfoModel { BlobId = condition.NewsBlobIdEng, BlobName = condition.NewsBlobNameEng, FileName = condition.NewsDocumentNameEng };
                    string PDFJSONENG = JsonConvert.SerializeObject(blobInfo);
                    vm.PDFFileEngJson = PDFJSONENG;
                }
                return View(vm);
            }
            else
            {
                return View(vm);
            }
        }

        [HttpPost]
        [AuthRequired(AuthorityId.NewsUpdate)]
        public async Task<IActionResult> NewsSearchAsync([FromBody]NewsSearchCondition condition)
        {
            var result = await _service.GetSearchResult(condition);
            TempData.Put(TEMP_NEWS_SEARCH_KEY, condition);
            return Ok(result);
        }

        [HttpGet]
        [AuthRequired(AuthorityId.NewsRefer)]
        [Route("~/api/News/GetPDF/{BlobId}")]
        public async Task<IActionResult> GetPDFByBlobId(int blobId)
        {
            var result = await _fileService.GetPDF(blobId);
            var memoryStream = new MemoryStream(result.Bytes);
            return new FileStreamResult(memoryStream, "application/pdf");
        }

        [HttpGet]
        [AuthRequired(AuthorityId.NewsRefer)]
        [Route("~/api/News/GetPDFByName/{BlobName}")]
        public async Task<IActionResult> GetPDFByBlobName(string blobName)
        {
            var result = await _fileService.GetPDF("news", blobName);
            var memoryStream = new MemoryStream(result);
            return new FileStreamResult(memoryStream, "application/pdf");
        }

        [HttpPost]
        [AuthRequired(AuthorityId.NewsUpdate)]
        [Route("~/api/News/UploadPDF/{containerName}")]
        public IActionResult UploadPDFWithNewsIdAsync(string containerName)
        {
            if (string.IsNullOrEmpty(Request.ContentType) || Request.ContentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return BadRequest($"Expected a multipart request, but got {Request.ContentType}");
            }

            var resultList = new List<BlobUploadResult>();
            var files = Request.Form.Files;
            if (files != null && files.Count != 0)
            {
                var user = "system" ?? IDENTITY_DEFAULT_NAME;
                var result = _fileService.UploadFilesWithBlobFileProperties(files, containerName, user, UserNow);
                result.Wait();
                //result.Result[0].FileName;
                resultList = result.Result;
            }

            return Ok(resultList);
        }

        [HttpPost]
        [AuthRequired(AuthorityId.NewsUpdate)]
        public IActionResult Register(NewsDetailViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                MergeToModelStateTransfers(vm);
                TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail));
            }

            var condition = _mapper.MapToNewsPutCondition(vm);
            SetPutConditionBase(condition);

            var result = _service.Register(condition);
            if (result.Succeeded)
            {
                vm.NewsId = result.NewsId;
                TempLastUpdatedAt = result.LastUpdatedAt;
            }
            else
            {
                SetMessages(vm, result);
                TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail));
            }

            SetMessages(vm, result);

            TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
            return RedirectToAction(nameof(Detail), new { newsId = vm.NewsId });
        }

        [HttpPost]
        [AuthRequired(AuthorityId.NewsUpdate)]
        public async Task<IActionResult> Update(NewsDetailViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                MergeToModelStateTransfers(vm);
                TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail), new { newsId = vm.NewsId });
            }

            var condition = _mapper.MapToNewsPutCondition(vm);
            SetPutConditionBase(condition);

            var result = _service.Update(condition);
            if (result.Succeeded)
            {
                TempLastUpdatedAt = result.LastUpdatedAt;
            }
            else
            {
                SetMessages(vm, result);
                TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail), new { newsId = vm.NewsId });
            }
            SetMessages(vm, result);
            TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
            return RedirectToAction(nameof(Detail), new { newsId = vm.NewsId });
        }

        [HttpPost]
        [AuthRequired(AuthorityId.NewsUpdate)]
        public IActionResult Delete(NewsDetailViewModel vm)
        {
            //再読み込み
            var condition = _mapper.MapToNewsPutCondition(vm);
            SetPutConditionBase(condition);

            var result = _service.Delete(condition);
            if (result.Succeeded)
            {
                TempLastUpdatedAt = result.LastUpdatedAt;
            }
            else
            {
                SetMessages(vm, result);
                TempData.Put(TEMP_NEWS_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail), new { newsId = vm.NewsId });
            }
            //var listvm = new NewsListViewModel();

            ////検索条件を保持していれば元に戻す
            //if (TempData.ContainsKey(TEMP_NEWS_SEARCH_KEY))
            //{
            //    listvm.SearchCondition = TempData.Get<NewsSearchCondition>(TEMP_NEWS_SEARCH_KEY);
            //}
            //else
            //{
            //    TempData.Clear();
            //}

            //SetMessages(listvm, result);

            var successMessage = result.SuccessMessage;
            TempData.Put(TEMP_NEWS_SUCCESS_KEY, successMessage);
            return RedirectToAction(nameof(List));
        }

        //[HttpPost]
        //public IActionResult DeletePDF(NewsDetailViewModel vm)
        //{
        //    var condition = _mapper.MapToNewsPutCondition(vm);
        //    SetPutConditionBase(condition);

        //    var result = _service.DeletePDF(condition);

        //    return RedirectToAction(nameof(Detail));
        //}
    }
}