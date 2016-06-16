﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using Microsoft.Ajax.Utilities;
using SkillsBook.Models.DAL;
using SkillsBook.Models.Models;

namespace SkillsBook.com.Controllers
{
    public class QuestionController : Controller
    {
        private readonly UnitOfWork _unitOfWork = new UnitOfWork();

        [HttpGet]
        public ActionResult Index()
        {
            var listOfCategories = new List<string>();
            var doc = new XmlDocument();
            doc.Load(Server.MapPath("~/LolNQuote/questionCategories.xml"));
            XmlElement xelRoot = doc.DocumentElement;
            if (xelRoot != null)
            {
                XmlNodeList xnlNodes = xelRoot.SelectNodes("/QuestionCategories/CategoriesList");
                if (xnlNodes != null)
                    listOfCategories.AddRange(from XmlNode xndNode in xnlNodes from XmlNode childNode in xndNode.ChildNodes select childNode.InnerText);
            }
            ViewBag.Categories = listOfCategories;
            return View();
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        [ValidateInput(false)]
        public ActionResult AskQuestion(string username, string email, string category, string question)
        {
            var scope = new TransactionScope(TransactionScopeOption.RequiresNew, new TransactionOptions()
            {
                IsolationLevel = IsolationLevel.ReadCommitted
            });
            try
            {
                if (String.IsNullOrEmpty(username) || String.IsNullOrEmpty(email) || String.IsNullOrEmpty(category) || String.IsNullOrEmpty(question))
                {
                    TempData["Error"] = "All fields are mandatory";
                    return RedirectToAction("Index", "Question");
                }
                var httpCookie = Request.Cookies["LogOnCookie"];
                if (httpCookie == null)
                {

                    if (_unitOfWork.DoesUserNameOnlyExist(username))
                    {
                        TempData["Error"] = "Username already exists";
                        TempData["Username"] = username;
                        TempData["Question"] = question;
                        TempData["Email"] = email;
                        return RedirectToAction("Index", "Question");
                    }
                    if (_unitOfWork.DoesEmailOnlyExist(email))
                    {
                        TempData["Error"] = "Email already exists";
                        TempData["Username"] = username;
                        TempData["Question"] = question;
                        TempData["Email"] = email;
                        return RedirectToAction("Index", "Question");
                    }
                    var pass = System.Web.Security.Membership.GeneratePassword(10, 2);
                    var passencrypted = Encryption.Encrypt(pass);
                    var userModel = new UserModel
                    {
                        Username = username,
                        Email = email,
                        Password = passencrypted,
                        ConfirmPassword = passencrypted,
                        IpAddress = GetIp(),
                        Browser = Request.Browser.Browser,
                        CreatedOn = DateTime.Now,
                        LastSuccessfulLogin = DateTime.Now
                    };
                    _unitOfWork.UserRepository.Insert(userModel);
                    _unitOfWork.SendEmail(email, "Your password for " + Constants.Domain, "Dear " + username + ",<br />" +
                        "You just posted a thread and thank you for your contribution. <br />Please find the auto generated temporary password." +
                        "<br />Temporary Password:" + pass +
                        "<br />You may want to login with this and change it once you login.<br /><br /> Thanking you <br />" +
                        Constants.Domain + "<br/>" + Constants.Slogan);

                }
              
                using (scope)
                {
                  
                    var questionModel = new QuestionModel
                    {
                        Username = username,
                        Email = email,
                        Category = category,
                        Question = question,
                        AskedOn = DateTime.Now,
                        IpAddress = GetIp(),
                        LastUpdated = DateTime.Now
                    };
                    _unitOfWork.QuestionRepository.Insert(questionModel);
                    _unitOfWork.Save();
                    Session["RecentQuestions"] = null;
                    scope.Complete();
                }
            }

            catch (Exception ex)
            {
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);
                scope.Dispose();
            }
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public ActionResult Answer(int questionId)
        {
            var recentQues = _unitOfWork.QuestionRepository.GetWithRawSql("Select Top 25 * from " + Constants.SchemaName +
                                                         "Questions order by LastUpdated desc");
            ViewBag.RecentQues = recentQues;
            var quesAns = _unitOfWork.GetAnswers(questionId);
            return View(quesAns);
        }

        [HttpGet]
        public ActionResult QuestionsOnly(int page = 1)
        {
            var offset = (page - 1) * Constants.BlocksizeMax;
            var questions = _unitOfWork.QuestionRepository.Get(x=>x.QuestionId > 0).Skip(offset).Take((int)(offset > Constants.BlocksizeMax ? offset - (offset - Constants.BlocksizeMax) : offset == 0 ? Constants.BlocksizeMax : offset)).OrderByDescending(x=>x.AskedOn);
            int totalPosts = _unitOfWork.QuestionRepository.Get(x=>x.QuestionId > 0).Count();
            ViewData["CurrentPage"] = page;
            ViewData["TotalCount"] = totalPosts;
            ViewData["BlockSize"] = Constants.BlocksizeMax;
            ViewData["TotalPages"] = ((totalPosts - 1) / Constants.BlocksizeMax) + 1;

            return View("QuestionsOnly",questions);
        }

       
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AnswerNow(int questionId,string answer)
        {
            if (String.IsNullOrEmpty(answer))
            {
                TempData["Invalid"] = "Answer is empty.";
                return RedirectToAction("Answer", "Question", new {questionId = questionId});
            }
            var httpCookie = Request.Cookies["LogOnCookie"];

            var questionModel = _unitOfWork.QuestionRepository.GetById(questionId);
            questionModel.TotalAnswers = questionModel.TotalAnswers + 1;
            _unitOfWork.QuestionRepository.Update(questionModel);
          
            var answerModel = new AnswerModel
            {
                QuestionQuestionId = questionId,
                Answer = answer,
                AnsweredBy = httpCookie == null ? "Anonymous" : httpCookie.Values["username"],
                AnsweredOn = DateTime.Now,
                IpAddress = GetIp(),
                LastUpdated = DateTime.Now,
                UsefulYes = 0,
                UsefulNo = 0,
                UserfulSomeWhat = 0
            };
            try
            {
                _unitOfWork.AnswerRepository.Insert(answerModel);
                _unitOfWork.Save();
                Session["RecentQuestions"] = null;
                _unitOfWork.SendEmail(questionModel.Email, "Someone just answered your question",
                        "Dear " + questionModel.Username +
                        "<br /> Someone just answered your question. <br /> Visit the link below to see the answer.<br />" +
                         Constants.Domain + "Question/Answer?questionId=" + questionModel.QuestionId + "<br /><br />" +
                        " With Best Regards <br />" +
                         Constants.Domain + "<br />" + Constants.Slogan);
            }
            catch (Exception ex)
            {
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);
            }

            return RedirectToAction("Answer", "Question", new {questionId = questionId});
        }

        [HttpGet]
        public JsonResult RateAnswer(int answerId, int rating)
        {
            try
            {
                var httpCookie = Request.Cookies["LogOnCookie"];
               // var cookieAnswer = new HttpCookie("AnsCookie");
                var answerModel = _unitOfWork.AnswerRepository.GetById(answerId);
                var count = 0;
                if (rating == Constants.Useful)
                {
                    answerModel.UsefulYes = answerModel.UsefulYes + 1;
                    count = answerModel.UsefulYes;
                }
                if (rating == Constants.Somewhat)
                {
                    answerModel.UserfulSomeWhat = answerModel.UserfulSomeWhat + 1;
                    count = answerModel.UserfulSomeWhat;
                }
                if (rating == Constants.NotUseful)
                {
                    answerModel.UsefulNo = answerModel.UsefulNo + 1;
                    count = answerModel.UsefulNo;
                }
                _unitOfWork.AnswerRepository.Update(answerModel);
                var ansResponseModel = new AnswerResponseModel
                {
                    AnswerAnswerId = answerId,
                    RatedBy = httpCookie == null ? "Anonymous" : httpCookie.Values["username"],
                    IpAddress = GetIp(),
                    RatedOn = DateTime.Now
                };
               
               // cookieAnswer.Values["Answer"] = "answer_" + answerId + "_" +
                 //                               (httpCookie == null ? "Anonymous" : httpCookie.Values["username"]);
                //cookieAnswer.Expires = DateTime.Now.AddDays(365);
                //Response.Cookies.Add(cookieAnswer);
                _unitOfWork.AnswerResponseRepository.Insert(ansResponseModel);
                _unitOfWork.Save();
                return Json(new {Result = "success",Count = count}, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);
                return Json(new { Result = "failed" }, JsonRequestBehavior.AllowGet);
            }

        }


        private static string GetIp()
        {
            var context = System.Web.HttpContext.Current;
            if (context == null)
                return string.Empty;

            return context.Request.ServerVariables["HTTP_X_FORWARDED_FOR"]
                   ?? context.Request.UserHostAddress;
        }

    }
}
