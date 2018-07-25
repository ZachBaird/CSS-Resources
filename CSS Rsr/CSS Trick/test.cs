using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using CMS.Scheduler;
using CMS.EventLog;
using CMS;
using CMS.WebServices;
using WorkdayAPI;
using WorkdayAPI.RecuritingService1;
using System.IO;
using System.Xml.Serialization;
using System.Xml;
using CMS.DocumentEngine;
using CMS.SiteProvider;
using CMS.Membership;
using CMS.Helpers;
using System.Data;
using System.Configuration;
/// <summary>
/// Summary description for WorkdayJobs
/// </summary>

[assembly: RegisterCustomClass("Custom.WorkdayJobs", typeof(WorkdayJobs))]
public class WorkdayJobs : ITask
{
    private RecuritingAPI oJob;
    private string sUser, sPass, sEndpoint, xmlDocument;


    public WorkdayJobs()
    {
        //
        // TODO: Add constructor logic here
        //
        sUser = ConfigurationManager.AppSettings["WorkdayAccountUsername"];
        sPass = ConfigurationManager.AppSettings["WorkdayAccountPassword"];
        sEndpoint = ConfigurationManager.AppSettings["WorkdayEndpoint"];
        xmlDocument = ConfigurationManager.AppSettings["WorkdayJobListingXMLFilePath"];
    }
    /// <summary>
    /// Executes the task.
    /// </summary>
    /// <param name="ti">Info object representing the scheduled task</param>
    public string Execute(TaskInfo ti)
    {
        string result = GetJobs();
        return result;
    }

    public string GetJobs()
    {
        string resultCount = string.Empty;
        
        oJob = new RecuritingAPI(sEndpoint, sUser, sPass);

        Get_Job_Postings_RequestType oRequest = new Get_Job_Postings_RequestType();
        WorkdayAPI.RecuritingService1.Response_FilterType oRespFilter = new WorkdayAPI.RecuritingService1.Response_FilterType();
        Job_Posting_Response_GroupType oRespGroup = new Job_Posting_Response_GroupType();
        
        Job_Posting_Site_Request_ReferencesType siteref = new Job_Posting_Site_Request_ReferencesType();
        
        Job_Posting_SiteObjectType jobsiteObj = new Job_Posting_SiteObjectType();
        Job_Posting_SiteObjectIDType ID = new Job_Posting_SiteObjectIDType();
        // TODO: need to investigate the purpose of those values below and if we can move them into the appSettings
        ID.type = "WID";
        ID.Value = "03ca6cbf24cc10fabe92ff4b5f82a0ac";

        jobsiteObj.ID = new Job_Posting_SiteObjectIDType[1];

        jobsiteObj.ID[0] = ID;
        siteref.Job_Posting_Site_Reference = new Job_Posting_SiteObjectType[1];
        siteref.Job_Posting_Site_Reference[0] = jobsiteObj;


        Job_Posting_Request_CriteriaType criteria = new Job_Posting_Request_CriteriaType();

        criteria.Job_Posting_Site_Reference = siteref.Job_Posting_Site_Reference;

        criteria.Show_Only_Active_Job_PostingsSpecified = true;
        criteria.Show_Only_Active_Job_Postings = true;
        oRequest.Request_Criteria = criteria;


        oRespFilter.Count = 999;
        oRespFilter.CountSpecified = true;

        
        oRequest.Response_Filter = oRespFilter;

        oRespGroup.Include_Reference = true;
        oRespGroup.Include_Job_Requisition_Restrictions_Data = true;
        oRespGroup.Include_Job_Requisition_Definition_Data = true;
        oRespGroup.Include_Qualifications = true;
        oRespGroup.Include_Job_Requisition_Attachments = false;

        oRequest.Response_Group = oRespGroup;
        Get_Job_Postings_ResponseType oResponse = oJob.GetJobPosting(oRequest, oRespFilter, oRespGroup);
        
        string jobsxml = XmlTools.ToXmlString(oResponse);
        
        jobsxml = jobsxml.Replace("utf-16", "utf-8");

	    xmlDocument = xmlDocument + DateTime.Now.Ticks + ".xml";
        using (System.IO.StreamWriter file = new System.IO.StreamWriter(xmlDocument))
        {
            file.Write(jobsxml);
        }

        XmlDocument doc = new XmlDocument();

        doc.Load(xmlDocument);
        
        var nodes = doc.GetElementsByTagName("d1p1:Job_Posting_Data");

        int jobsCreatedCount = 0;
        int jobsUpdatedCount = 0;
        int jobsErrorCount = 0;
        int jobsDeletedCount = 0;
        string jobsDeletedID = "", jobsUpdatedID = "", jobsErrorID = "", jobsCreatedID = "";

        TreeProvider tree = new TreeProvider(MembershipContext.AuthenticatedUser);
        TreeNode parentNode = tree.SelectSingleNode(SiteContext.CurrentSiteName, "/Company/Jobs", "en-us");

        DocumentQuery cmsjobs = DocumentHelper.GetDocuments("AT.Jobs")
                                       .Path("/Company/Jobs", PathTypeEnum.Children)
                                       .OnSite(SiteContext.CurrentSiteName);


        foreach (XmlNode xn in nodes)
        {
            var jobPostingID = xn.ChildNodes.Item(0).InnerText.ToString();

            try
            {
                    bool isJobInCMS = false;

                    foreach (var jobs in cmsjobs) 
                    {
                        string jobsID = jobs.GetValue("Job_Posting_ID").ToString();
                        

                        if (jobsID.ToString().ToLower() == xn.ChildNodes.Item(0).InnerText.ToString().ToLower())
                        {
                            if (parentNode != null)
                            {
                               
                                var documents = DocumentHelper.GetDocuments()
                                            .Types("AT.Jobs")
                                            .Path("/Company/Jobs", PathTypeEnum.Children)
                                            .WhereLike("Job_Posting_ID", jobPostingID)
                                            .OnSite(SiteContext.CurrentSiteName)
                                            .Culture("en-us")
                                            .WithCoupledColumns();

                                if (!DataHelper.DataSourceIsEmpty(documents) && documents.Tables[0].Rows.Count == 1)
                                {
                                    // Loop through all documents
                                    foreach (DataRow documentRow in documents.Tables[0].Rows)
                                    {
                                        // Create a new Tree node from the data row
                                        CMS.DocumentEngine.TreeNode editDocument = CMS.DocumentEngine.TreeNode.New("AT.Jobs", documentRow, tree);
                                        // Change coupled data
                                        SetNodeValue(xn, editDocument);

                                        editDocument.Update();

                                    }
                                }

                                jobsUpdatedCount++;
                                jobsUpdatedID += jobPostingID + "\r\n";
                            }
                            isJobInCMS = true;
                            break;
                        }
                    }
                    if (!isJobInCMS)
                    {
                        if (parentNode != null)
                        {
                            // Create a new instance of the Tree node
                            CMS.DocumentEngine.TreeNode newNode = CMS.DocumentEngine.TreeNode.New("AT.Jobs", tree);
                            
                            // Set the document's properties
                            newNode.DocumentName = xn.ChildNodes.Item(1).InnerText;
                            newNode.DocumentCulture = "en-us";

                            SetNodeValue(xn, newNode);

                            newNode.Insert(parentNode);
                            jobsCreatedCount++;
                            jobsCreatedID += jobPostingID + "\r\n";
                        }
                        else
                            EventLogProvider.LogInformation("No parent", "No parent", "Page can't be created as there is no parent page.");
                    }

                
            }
            catch (Exception ex)
            {
                
                jobsErrorCount++;
                jobsErrorID += jobPostingID + "\r\n";
            }
            finally
            {
                CleanupXMLFIles();
            }
        }

        string result = jobsCreatedCount + " Jobs were created and " + jobsUpdatedCount + " Jobs were updated and " + jobsDeletedCount + " Jobs were deleted and " + jobsErrorCount + " Jobs have errors.";

        string reportResults = String.Format("{0} \r\n\r\n========List of jobs created IDs========\r\n\r\n {1}\r\n========List of jobs updated IDs========\r\n\r\n {2}\r\n========List of jobs deleted IDs========\r\n\r\n {3}\r\n========List of jobs with error IDs========\r\n\r\n {4}",
            result, jobsCreatedID, jobsUpdatedID, jobsDeletedID, jobsErrorID);
        EventLogProvider.LogEvent(EventType.INFORMATION, "Workday", "Workday Import Report", reportResults);
        return result;
    }

    /// <summary>
    /// Delete all of the XML files that were created by the import process of the workday API based on the retention days setting on the appSettings.
    /// </summary>
    private void CleanupXMLFIles()
    {
        string dirPath = ConfigurationManager.AppSettings["WorkdayJobListingXMLFilePath"];
        int retentionDays;
        int.TryParse(ConfigurationManager.AppSettings["WorkdayXMLRetentionDays"], out retentionDays);

        foreach (string file in Directory.GetFiles(dirPath))
        {
            FileInfo fi = new FileInfo(file);
            if (fi.CreationTime < DateTime.Now.AddDays(-retentionDays))
                fi.Delete();
        }
    }
    /// <summary>
    /// Set all of the job page with the required values based on the Workday API called.
    /// </summary>
    /// <param name="xn"></param>
    /// <param name="editDocument"></param>
    private void SetNodeValue(XmlNode xn, TreeNode editDocument)
    {
        editDocument.SetValue("Job_Posting_ID", ValidationHelper.GetString(xn.ChildNodes.Item(0).InnerText, ""));
        editDocument.SetValue("DocumentName", ValidationHelper.GetString(xn.ChildNodes.Item(1).InnerText, ""));
        editDocument.SetValue("Location", ValidationHelper.GetString(xn.ChildNodes[8].ChildNodes[0].ChildNodes[1].InnerText, ""));
        editDocument.SetValue("Job_Family_ID", ValidationHelper.GetString(xn.ChildNodes[12].ChildNodes[1].InnerText, ""));
        editDocument.SetValue("Description", ValidationHelper.GetString(xn.ChildNodes.Item(2).InnerText, ""));

        editDocument.SetValue("DateCreated", ValidationHelper.GetString(xn.ChildNodes.Item(9).InnerText, ""));
        editDocument.SetValue("External_Job_Path", ValidationHelper.GetString(xn.ChildNodes.Item(4).InnerText, ""));
        editDocument.SetValue("External_Apply_URL", ValidationHelper.GetString(xn.ChildNodes.Item(5).InnerText, ""));
        editDocument.SetValue("JobType", ValidationHelper.GetString(xn.ChildNodes[13].ChildNodes[1].InnerText, ""));
    }
}
public static class XmlTools
{
    public static string ToXmlString<T>(this T input)
    {
        using (var writer = new StringWriter())
        {
            input.ToXml(writer);
            return writer.ToString();
        }
    }
    public static void ToXml<T>(this T objectToSerialize, Stream stream)
    {
        new XmlSerializer(typeof(T)).Serialize(stream, objectToSerialize);
    }

    public static void ToXml<T>(this T objectToSerialize, StringWriter writer)
    {
        new XmlSerializer(typeof(T)).Serialize(writer, objectToSerialize);
    }
}