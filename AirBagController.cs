using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Jevic.Common.Enums;
using Jevic.Common.Models;
using Jevic.Core.Contracts.Models.AirBag;
using Jevic.Core.Contracts.Models.Blob;
using Jevic.Core.Contracts.Models.Common;
using Jevic.Core.Contracts.Services;
using Jevic.Web.Attributes;
using Jevic.Web.Extensions;
using Jevic.Web.Mappers;
using Jevic.Web.Properties;
using Jevic.Web.ViewModels.AirBag;
using Microsoft.AspNetCore.Mvc;

namespace Jevic.Web.Controllers
{
    public class AirBagController : AuthorizedController
    {
        private readonly IAirBagService _service;
        private readonly IViewModelMapper _mapper;
        private readonly IBlobFileService _fileService;

        public static string TEMP_AIRBAG_SEARCH_KEY = nameof(TEMP_AIRBAG_SEARCH_KEY);
        private const string TEMP_AIRBAG_DETAIL_KEY = nameof(TEMP_AIRBAG_DETAIL_KEY);
        public static string TEMP_AIRBAG_SUCCESS_KEY = nameof(TEMP_AIRBAG_SUCCESS_KEY);
        private const string TEMP_ERROR_EXCEL_KEY = nameof(TEMP_ERROR_EXCEL_KEY);
        private const string IDENTITY_DEFAULT_NAME = "AnonymousErrorUser";

        public AirBagController(IAirBagService service, IViewModelMapper mapper, IBlobFileService fileService)
        {
            _service = service;
            _mapper = mapper;
            _fileService = fileService;
        }

        [HttpGet]
        [AuthRequired(AuthorityId.AirbagRefer)]
        public IActionResult List()
        {
            var vm = new AirBagListViewModel();
            if (TempData.ContainsKey(TEMP_AIRBAG_SEARCH_KEY))
            {
                MergeToModelState(vm);
                var airBagSearchCondition = TempData.Get<AirBagSearchCondition>(TEMP_AIRBAG_SEARCH_KEY);
                var successMessage = TempData.Get<string>(TEMP_AIRBAG_SUCCESS_KEY);
                if (successMessage != null)
                {
                     vm = new AirBagListViewModel { SearchCondition = airBagSearchCondition, SuccessMessage = successMessage };
                    
                    
                }
                else
                {
                     vm = new AirBagListViewModel { SearchCondition = airBagSearchCondition };
                    
                    //return View(new AirBagListViewModel { SearchCondition = TempData.Get<AirBagSearchCondition>(TEMP_AIRBAG_SEARCH_KEY) });
                }
            }

            TempData.Clear();
            return View(vm);
        }

        [HttpPost]
        [AuthRequired(AuthorityId.AirbagUpdate)]
        public async Task<IActionResult> AirBagSearchAsync([FromBody]AirBagSearchCondition condition)
        {
            var result = await _service.GetSearchResult(condition);
            TempData.Put(TEMP_AIRBAG_SEARCH_KEY, condition);
            return Ok(result);
        }

        [HttpGet]
        [AuthRequired(AuthorityId.AirbagRefer)]
        public async Task<IActionResult> Detail(string airBagId)
        {
            var vm = new AirBagDetailViewModel();

            //PostBack時
            if (TempData.ContainsKey(TEMP_AIRBAG_DETAIL_KEY))
            {
                vm = TempData.Get<AirBagDetailViewModel>(TEMP_AIRBAG_DETAIL_KEY);
                MergeToModelState(vm);
                return View(vm);
            }
            //更新の場合
            if (!string.IsNullOrEmpty(airBagId) && int.TryParse(airBagId, out var id))
            {
                var condition = await _service.GetByAirBagId(id);
                TempLastUpdatedAt = condition.LastUpdatedAt;
                vm = _mapper.MapToAirBagDetailViewModel(condition);
                return View(vm);
            }
            else
            {
                return View(vm);
            }
        } /*Detail*/

        [HttpPost]
        [AuthRequired(AuthorityId.AirbagUpdate)]
        public IActionResult Register(AirBagDetailViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                MergeToModelStateTransfers(vm);
                TempData.Put(TEMP_AIRBAG_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail));
            }
            var condition = _mapper.MapToAirBagPutCondition(vm);
            SetPutConditionBase(condition);

            var result = _service.Register(condition);
            if (result.Succeeded)
            {
                vm.AirBagId = result.AirBagId;
                // ポストバック時の排他によるエラー回避のためTempUpdatedAtに登録日時を詰める
                TempLastUpdatedAt = result.LastUpdatedAt;
            }
            SetMessages(vm, result);
            TempData.Put(TEMP_AIRBAG_DETAIL_KEY, vm);
            return RedirectToAction(nameof(Detail), new { airBagId = vm.AirBagId });
        } /*Register*/

        [HttpPost]
        [AuthRequired(AuthorityId.AirbagUpdate)]
        public IActionResult Update(AirBagDetailViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                MergeToModelStateTransfers(vm);
                TempData.Put(TEMP_AIRBAG_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail));
            }
            var condition = _mapper.MapToAirBagPutCondition(vm);
            SetPutConditionBase(condition);

            var result = _service.Update(condition);

            if (result.Succeeded)
            {
                // ポストバック時の排他によるエラー回避のためTempUpdatedAtに登録日時を詰める
                TempLastUpdatedAt = result.LastUpdatedAt;
            }
            SetMessages(vm, result);
            TempData.Put(TEMP_AIRBAG_DETAIL_KEY, vm);
            return RedirectToAction(nameof(Detail), new { airBagId = vm.AirBagId });
        }

        [HttpPost]
        [AuthRequired(AuthorityId.AirbagUpdate)]
        public async Task<IActionResult> RegisterAirBagEx()
        {
            //Excel習得
            //var dataReader = new AirBagDetailDataReader(Request.Form.Files[0]);
            //try
            //{
            //    dataReader.LoadFile();
            //    //var validateResult = dataReader.ValidateFormat();

            //    //if (validateResult.ResultType != ResultType.Success)
            //    //{
            //    //    return Json(validateResult);
            //    //}
            //}
            //catch
            //{
            //    var errorResult = new CommonResult<FileResultBase>
            //    {
            //        ResultType = ResultType.Error,
            //        ErrorMessages = new List<string> { Resources.E_FailedLoadingFile }
            //    };
            //    return Json(errorResult);
            //}

            var condition = new AirBagFilePutCondition();

            SetPutConditionBase(condition);

            //condition.AirBagDetails = dataReader.GetContent();



            //Excel Upload blob
            if (string.IsNullOrEmpty(Request.ContentType) || Request.ContentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return BadRequest($"Expected a multipart request, but got {Request.ContentType}");
            }

            var resultList = new List<BlobUploadResult>();
            var files = Request.Form.Files;

            if (files != null && files.Count != 0)
            {
                var user = "system" ?? IDENTITY_DEFAULT_NAME;
                var blobResult = _fileService.UploadExcelFilesWithBlobFileProperties(files, "airbag", user, UserNow);
                blobResult.Wait();
                //result.Result[0].FileName;
                resultList = blobResult.Result;
            }
            DateTime now = condition.UserNow;

            var result = new CommonResult<FileResultBase>();
            //register  Excel File in DB
            if (resultList.Count > 0)
            {
                foreach (var uploadResult in resultList)
                {
                    var Registeredresult = _service.RegisterFilesWithBlobFileProperties(uploadResult.BlobName, uploadResult.FileName, "airbag", now, "system");
                    uploadResult.BlobId = Registeredresult.BlobId;
                }
                var saveExcelInfo = _service.RegisterFileInfo(resultList, now, condition.UpdatedBy);
                result.ResultType = ResultType.Success;
                result.SuccessMessage = saveExcelInfo.SuccessMessage;
            }



            //var registerResult1 = _service.RegisterAirBag();
            else
            {
                result.ResultType = ResultType.Error;
                result.ErrorMessages.Add(Resources.E_FailSaveAirBagFileInStorage);
            }


            //Excelポマトチャック
            //var checkResult = dataReader.FormatCheck();
            //if (checkResult.ResultType == ResultType.Error)
            //{
            //    var errorResult = new CommonResult<FileResultBase>
            //    {
            //        ResultType = ResultType.Error,
            //        ErrorMessages = new List<string> { Resources.E_ExcelFormatError }
            //    };
            //    return Json(errorResult);
            //}

            //var validationResult = _service.ValidateCondition(condition);

            //if (!validationResult.Succeeded && validationResult.Result != null)
            //{
            //    validationResult.Result.FileName = dataReader.GetFileName();

            //    TempData.Put(TEMP_ERROR_EXCEL_KEY, validationResult.Result);
            //}

            //var registerResult = await _service.RegisterAirBag(condition);

            ////処理結果生成
            
            //result.ErrorMessages = new List<string>();

            //if (validationResult.ResultType != ResultType.Success)
            //{
            //    result.Result = validationResult.Result;
            //    result.ErrorMessages.AddRange(validationResult.ErrorMessages);
            //}

            //if (registerResult.ResultType == ResultType.Success)
            //{
            //    result.SuccessMessage = registerResult.SuccessMessage;
            //}
            //else
            //{
            //    result.ErrorMessages.AddRange(registerResult.ErrorMessages);
            //}
            return Json(result);
        }
        [HttpPost]
        [AuthRequired(AuthorityId.AirbagUpdate)]

        public CommonResult StartAirBagFileWebJob()
        {
            var registerResult = _service.RegisterAirBag();
            return registerResult;
        }

        [HttpGet]
        [AuthRequired(AuthorityId.AirbagRefer)]
        public IActionResult OutputAirbagErrorExcel()
        {
            if (!TempData.ContainsKey(TEMP_ERROR_EXCEL_KEY))
            {
                return BadRequest();
            }

            var file = TempData.Get<FileResultBase>(TEMP_ERROR_EXCEL_KEY);

            return File(file.Bytes, file.ContentType, file.FileName);
        }

        [HttpPost]
        [AuthRequired(AuthorityId.AirbagUpdate)]
        public IActionResult Delete(AirBagDetailViewModel vm)
        {
            //再読み込み
            var condition = _mapper.MapToAirBagPutCondition(vm);
            SetPutConditionBase(condition);

            var result = _service.Delete(condition);
            if (result.Succeeded)
            {
                TempLastUpdatedAt = result.LastUpdatedAt;
            }
            else
            {
                SetMessages(vm, result);
                TempData.Put(TEMP_AIRBAG_DETAIL_KEY, vm);
                return RedirectToAction(nameof(Detail), new { airbagId = vm.AirBagId });
            }

            var successMessage = result.SuccessMessage;
            TempData.Put(TEMP_AIRBAG_SUCCESS_KEY, successMessage);
            return RedirectToAction(nameof(List));
        }


        [HttpGet]
        [AuthRequired(AuthorityId.AirbagRefer)]
        public IActionResult GetAirBagFileHistory()
        {
            var getHistoryresult = _service.GetAllFileHistory();

            return Ok(getHistoryresult);
        }
    }/*controller*/
}