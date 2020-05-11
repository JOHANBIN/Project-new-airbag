using AutoMapper;
using ClosedXML.Excel;
using Jevic.Common.Consts;
using Jevic.Common.Enums;
using Jevic.Common.Models;
using Jevic.Core.Contracts.Models.AirBag;
using Jevic.Core.Contracts.Models.Common;
using Jevic.Core.Contracts.Services;
using Jevic.Core.Services;
using Jevic.Data.Contracts;
using Jevic.Data.Contracts.Entities;
using Jevic.WebJob.AirBagFile.Contracts.Services;
using Jevic.WebJob.AirBagFile.Properties;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jevic.WebJob.AirBagFile.Services
{
    class AirBagFileService : IAirBagFileService
    {

        private readonly IUnitOfWork _uow;
        private readonly IBlobFileService _fileService;
        private readonly ILogger _logger;

        public AirBagFileService(IUnitOfWork uow, IBlobFileService fileService, ILogger<AirBagFileService> logger)
        {
            _uow = uow;
            _fileService = fileService;
            _logger = logger;
        }

        public CommonResult CheckAndSaveAirBagFile()
        {
            _logger.LogInformation("********** CheckAndSaveAirBagFile is Started!! **********");
            var result = new CommonResult<FileResultBase>();


            var blobIdQueries = _uow.AirBagFiles.GetAll().Where(x => x.UploadStatus == (int)UploadStatusCode.Before).ToList();
            //before statusがnull場合
            if (blobIdQueries.Count == 0)
            {
                _logger.LogInformation("********** Can't find anyting **********");
                return new CommonResult { ResultType = ResultType.Error, ErrorMessages = { Resources.E_NoQueryError } };

            }


            foreach (var blobIdQuery in blobIdQueries)
            {
                blobIdQuery.UploadStatus = (int)UploadStatusCode.InProcess;
                blobIdQuery.UpdatedAt = DateTime.Now.AddHours(9);
                _uow.SaveChanges();

                if (blobIdQuery.BlobId != 0)
                {
                    var blobInfo = _fileService.GetBlobName("airbag", blobIdQuery.BlobId);
                    var resultBlobFile = _fileService.DownloadFileFromBlob("airbag", blobInfo.BlobName);
                    var memoryStream = new MemoryStream(resultBlobFile.Result);
                    var dataReader = new AirBagDetailDataReader();

                    try
                    {
                        dataReader.LoadFile(memoryStream);
                        //var validateResult = dataReader.ValidateFormat();

                        //if (validateResult.ResultType != ResultType.Success)
                        //{
                        //    return Json(validateResult);
                        //}
                    }
                    catch
                    {
                        blobIdQuery.UploadStatus = (int)UploadStatusCode.Error;
                        blobIdQuery.ErrorMessage = Resources.E_FailedLoadingFile;
                        blobIdQuery.UpdatedAt = DateTime.Now.AddHours(9);
                        //_uow.SaveChanges();

                        var errorResult = new CommonResult<FileResultBase>
                        {
                            ResultType = ResultType.Error,
                            ErrorMessages = new List<string> { Resources.E_FailedLoadingFile }
                        };
                        return errorResult;
                    }

                    var condition = new AirBagFilePutCondition();

                    //SetPutConditionBase(condition);

                    condition.AirBagDetails = dataReader.GetContent();

                    var validationResult = ValidateCondition(condition);


                    //if (!validationResult.Succeeded && validationResult.Result != null)
                    //{
                    //    validationResult.Result.FileName = dataReader.GetFileName();

                    //    TempData.Put(TEMP_ERROR_EXCEL_KEY, validationResult.Result);
                    //}

                    var registerResult = RegisterAirBag(condition, blobIdQuery.CreatedBy, blobIdQuery.CreatedAt);

                    //処理結果生成
                    //result = new CommonResult<FileResultBase>();
                    result.ErrorMessages = new List<string>();
                    //validationチェックでエラー
                    if (validationResult.ResultType != ResultType.Success)
                    {
                        var uploadErrorFileResult = _fileService.UploadStreamExcel(validationResult.Result.Bytes, "ErrorExcel.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "airbag", blobIdQuery.UpdatedBy, blobIdQuery.UpdatedAt ?? DateTime.Now);
                        var uploadPropertyresult = _fileService.RegisterFilesWithBlobFileProperties(uploadErrorFileResult.Result.BlobName, uploadErrorFileResult.Result.FileName, "airbag", DateTime.Now, "system");
                        blobIdQuery.UploadStatus = (int)UploadStatusCode.Error;
                        blobIdQuery.ErrorBlobId = uploadPropertyresult.BlobId;
                        blobIdQuery.ErrorMessage = Resources.E_FileValidationError;
                        //一部格納成功
                        if (registerResult.ResultType == ResultType.Success)
                        {
                            result.SuccessMessage = registerResult.SuccessMessage;
                        }

                        result.Result = validationResult.Result;
                        result.ErrorMessages.AddRange(validationResult.ErrorMessages);
                        blobIdQuery.UpdatedAt = DateTime.Now.AddHours(9);
                    }

                    //全件格納成功
                    else if (registerResult.ResultType == ResultType.Success && validationResult.ResultType == ResultType.Success)
                    {
                        blobIdQuery.UploadStatus = (int)UploadStatusCode.Done;
                        result.SuccessMessage = registerResult.SuccessMessage;
                        blobIdQuery.UpdatedAt = DateTime.Now.AddHours(9);

                    }

                    //全件失敗
                    else
                    {
                        blobIdQuery.UploadStatus = (int)UploadStatusCode.Error;
                        result.ErrorMessages.AddRange(registerResult.ErrorMessages);
                        blobIdQuery.ErrorMessage = registerResult.ErrorMessages[0];
                        blobIdQuery.UpdatedAt = DateTime.Now.AddHours(9);

                    }
                    _uow.SaveChanges();
                    LogComplete();
                    //return result;
                }


            }//foreach


            return result;
        }

        private void LogComplete()
        {
            _logger.LogInformation("********** CheckAndSaveAirBagFile is Completed!! **********");
        }


        public CommonResult<FileResultBase> ValidateCondition(AirBagFilePutCondition condition)
        {
            var result = new CommonResult<FileResultBase>();
            var validateResult = _ValidateAirBagCondition(condition);

            if (validateResult.ResultType == ResultType.Success)
            {
                result.ResultType = ResultType.Success;
            }
            else
            {
                result.ErrorMessages = validateResult.ErrorMessages;
                result.ResultType = ResultType.Error;
                result.Result = new FileResultBase
                {
                    Bytes = _CreateErrorExcelByte(condition),
                    ContentType = ContentType.Xlsx,
                    ResultType = ResultType.Error
                };
            }

            return result;
        }

        private CommonResult _ValidateAirBagCondition(AirBagFilePutCondition condition)
        {
            var result = new CommonResult<AirBagFilePutCondition>();
            var airBagDetails = condition.AirBagDetails.ToList();
            var status = _uow.RecallStatuses.GetAll().Select(x => x.RecallStatusName).ToList();
            _logger.LogInformation("#########################InPut And Duplication Checking#######################");
            foreach (var row in airBagDetails)
            {

                _InputCheck(row, status);
                _DuplicateCheckInFile(row, airBagDetails);


                //質問
            }

            var errorCount = airBagDetails.Where(x => !string.IsNullOrEmpty(x.ErrorMessages)).Count();

            if (errorCount == 0)
            {
                result.ResultType = ResultType.Success;
            }
            else
            {
                result.ResultType = ResultType.Error;
                result.ErrorMessages.Add(string.Format(Resources.E_RegisterErrorCount, errorCount));
            }
            return result;
        }

        private void _InputCheck(AirBagDetailInputFile row, List<string> status)
        {

            var colIndex = new AirBagDetailInputFileMap().MemberMaps.ToDictionary(x => x.Data.Member.Name, x => x.Data.Index + 1);
            var errorMessages = new List<string>();

            //エラー処理をカプセル化
            void addError(string columnName, string msg) => errorMessages.Add(string.Format(Resources.Format_ColIndex, colIndex[columnName], msg));
            //必須チェック
            void requiredCheck(string value, string columnName)
            {
                if (string.IsNullOrEmpty(value)) addError(columnName, Resources.E_Required);
            }
            //文字数チェック
            void lengthCheck(string value, string columnName, int maxLength)
            {
                if (!string.IsNullOrEmpty(value) && value.Length > maxLength)
                    addError(columnName, string.Format(Resources.E_StringLength, maxLength));
            }
            //DateTime刑チェック
            void dateCheck(DateTime? value, string columnName)
            {
                //DateTime dt;
                //bool success = DateTime.TryParseExact(value.ToString(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

                if (value == null)
                    addError(columnName, Resources.E_DateFormat);
            }

            //Result形式チェック
            void recallCheck(string value, string columnName)
            {
                if (!status.Contains(value))
                {
                    addError(columnName, Resources.E_StatusName);
                }
            }

            void nonAlpharecallCheck(string value, string columnName)
            {

                if (!status.Contains(value))
                {
                    addError(columnName, Resources.E_NonAlphaStatusName);
                }
            }

            void halfWidthString(string value, string columnName)
            {
                Encoding sjisEnc = Encoding.GetEncoding("Shift_JIS");

                int num = sjisEnc.GetByteCount(value);
                if (num == value.Length * 2)
                    addError(columnName, Resources.E_HalfString);
            }

            //必須チェック
            requiredCheck(row.CarMakerNameEng, nameof(row.CarMakerNameEng));
            requiredCheck(row.CarModelName, nameof(row.CarModelName));
            requiredCheck(row.ChassisNo, nameof(row.ChassisNo));
            requiredCheck(row.InspectionDateText, nameof(row.InspectionDateText));
            requiredCheck(row.RecallStatusName, nameof(row.RecallStatusName));
            requiredCheck(row.NonAlphaRecallStatusName, nameof(row.NonAlphaRecallStatusName));

            //文字数チェック
            lengthCheck(row.CarMakerNameEng, nameof(row.CarMakerNameEng), 100);
            lengthCheck(row.CarModelName, nameof(row.CarModelName), 100);
            lengthCheck(row.ChassisNo, nameof(row.ChassisNo), 100);
            lengthCheck(row.InspectionDateText, nameof(row.InspectionDateText), 100);
            lengthCheck(row.RecallStatusName, nameof(row.RecallStatusName), 100);
            lengthCheck(row.NonAlphaRecallStatusName, nameof(row.NonAlphaRecallStatusName), 100);
            //半角チェック
            halfWidthString(row.ChassisNo, nameof(row.ChassisNo));
            //DateTime刑チェック
            //dateCheck(row.InspectionDate, nameof(row.InspectionDateText));
            dateCheck(row.InspectionDate, nameof(row.InspectionDateText));
            //Result形式チェック
            recallCheck(row.RecallStatusName, nameof(row.RecallStatusName));
            nonAlpharecallCheck(row.NonAlphaRecallStatusName, nameof(row.NonAlphaRecallStatusName));

            if (errorMessages.Any())
            {
                row.ErrorMessages += string.Join(",", errorMessages);
            }
        } /*_InputCheck*/

        private void _DuplicateCheckInFile(AirBagDetailInputFile row, List<AirBagDetailInputFile> searchResult)
        {
            var chassisNoSearchResult = searchResult.Where(x => x.ChassisNo == row.ChassisNo).ToList();

            if (chassisNoSearchResult.Count > 1)
            {
                row.ErrorMessages += Resources.E_ChassisNoDuplicateInFile;
            }
        }

        private byte[] _CreateErrorExcelByte(AirBagFilePutCondition condition)
        {
            var workBook = new XLWorkbook();
            var workSheet = workBook.Worksheets.Add("ErrorFeedBacks");
            var colIndex = new AirBagDetailInputFileMap().MemberMaps.ToDictionary(x => x.Data.Member.Name, x => x.Data.Index + 1);
            var errorList = condition.AirBagDetails.Where(x => !string.IsNullOrEmpty(x.ErrorMessages)).ToList();

            workSheet.Range("A1:F1").Style.Fill.BackgroundColor = XLColor.PastelOrange;
            workSheet.Style.Font.FontName = "游ゴシック";

            workSheet.Cell(1, 1).Value = "Make of Vehicle";
            workSheet.Cell(1, 2).Value = "Model of Vehicle";
            workSheet.Cell(1, 3).Value = "Manifest VIN";
            workSheet.Cell(1, 4).Value = "Border Checked";
            workSheet.Cell(1, 5).Value = "Alpha";
            workSheet.Cell(1, 6).Value = "Non-Alpha";

            var rowIndex = 2;

            foreach (var row in condition.AirBagDetails)
            {
                if (string.IsNullOrEmpty(row.ErrorMessages)) continue;

                workSheet.Cell(rowIndex, colIndex[nameof(row.CarMakerNameEng)]).Value = row.CarMakerNameEng;
                workSheet.Cell(rowIndex, colIndex[nameof(row.CarModelName)]).Value = row.CarModelName;
                workSheet.Cell(rowIndex, colIndex[nameof(row.ChassisNo)]).Value = row.ChassisNo;
                //workSheet.Cell(rowIndex, colIndex[nameof(row.InspectionDateText)]).Value = row.InspectionDate?.ToString("dd/M/yyyy");
                workSheet.Cell(rowIndex, colIndex[nameof(row.InspectionDateText)]).Style.NumberFormat.SetFormat("dd/MM/yyyy");
                workSheet.Cell(rowIndex, colIndex[nameof(row.InspectionDateText)]).Value = row.InspectionDateText;
                workSheet.Cell(rowIndex, colIndex[nameof(row.RecallStatusName)]).Value = row.RecallStatusName;
                workSheet.Cell(rowIndex, colIndex[nameof(row.NonAlphaRecallStatusName)]).Value = row.NonAlphaRecallStatusName;
                workSheet.Cell(rowIndex, colIndex[nameof(row.ErrorMessages)]).Value = row.ErrorMessages;

                rowIndex++;
            }

            workSheet.Columns("A:F").AdjustToContents();

            var bytes = new byte[0];

            using (var ms = new MemoryStream())
            {
                workBook.SaveAs(ms);
                bytes = ms.ToArray();
            }

            return bytes;
        }

        public CommonResult RegisterAirBag(AirBagFilePutCondition condition, string UpdatedBy, DateTime? UserNow)
        {
            _logger.LogInformation("#########################RegisterAirBag Checking#######################");
            //登録できるデータがなければ結果なしでリターンん
            var registableData = condition.AirBagDetails.Where(x => string.IsNullOrEmpty(x.ErrorMessages)).ToList();

            if (!registableData.Any())
            {
                return new CommonResult()
                {
                    ResultType = ResultType.Error,
                    ErrorMessages = { Resources.E_RegisterDataError }

                };

            };
            //using (var tran = _uow.BeginTran())
            //{
            //HashSet<string> chassissNoList = new HashSet<string>(_uow.AirBags.GetAll().Select(x => x.ChassisNo));
            List<AirBag> mappedRegistableData = new List<AirBag>();
            List<AirBag> mappedUpdatableData = new List<AirBag>();
            var mappedData = Mapper.Map<List<AirBag>>(registableData);
            mappedData.ForEach(airBagDetail =>
            {
                airBagDetail.CreatedAt = UserNow;
                airBagDetail.UpdatedAt = UserNow;
                airBagDetail.CreatedBy = UpdatedBy;
                airBagDetail.UpdatedBy = UpdatedBy;
            });
            var chassisNoList = mappedData.Select(x => x.ChassisNo);

            //var findAirBagID = _uow.AirBags.Find(x => chassisNoList.Contains(x.ChassisNo)).Select(x=>x.AirBagId);
            //_uow.AirBags.Remove(x => findAirBagID.Contains(x.AirBagId));


            //var IsChassisMappedData = mappedData.Where(x => chassisNoList.Contains(x.ChassisNo)).ToList();

            _uow.AirBags.Remove(x => chassisNoList.Contains(x.ChassisNo));
            _logger.LogInformation("#########################RegisterAirBag2 Checking#######################");
            _uow.AirBags.AddRange(mappedData);


            //mappedRegistableData = mappedData.Where(x => !chassissNoList.Contains(x.ChassisNo)).ToList();

            ////foreach (var row in registableData)
            ////{
            //    row.ChassisNo = row.ChassisNo.Trim();

            //    if (!chassissNoList.Contains(row.ChassisNo))
            //    {
            //        mappedRegistableData.Add(mappedData);
            //    }
            //    else
            //    {
            //        mappedUpdatableData.Add(mappedData);
            //    }

            ////}


            //foreach (var row in registableData)
            //{   //chassisNo空白削除

            //var searchResult = await _uow.AirBags.GetByParamAsync(row.ChassisNo);

            //if (!chassissNoList.Contains(row.ChassisNo))
            //{
            //upsertResult = _RegisterAirBagDetail(condition, mappedRegistableData);
            //}
            //else
            //{
            //upsertResult = await _UpdateAirBagDetail(condition, mappedUpdatableData);
            //}
            //}
            _uow.SaveChanges();
            //tran.Commit();
            //}

            return new CommonResult
            {
                LastUpdatedAt = UserNow,
                ResultType = ResultType.Success,
                SuccessMessage = Resources.S_Registerd
            };
        }

    }
}
