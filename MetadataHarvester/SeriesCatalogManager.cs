﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Net;

namespace MetadataHarvester
{
    class SeriesCatalogManager
    {
        private Dictionary<string, GhcnSite> _siteLookup;

        private Dictionary<string, GhcnVariable> _variableLookup;

        public SeriesCatalogManager()
        {
            // initialize the site and variable lookup
            _siteLookup = getSiteLookup();
            _variableLookup = getVariableLookup();

        }

        private Dictionary<string, GhcnSite> getSiteLookup()
        {
            Dictionary<string, GhcnSite> lookup = new Dictionary<string, GhcnSite>();

            string connString = ConfigurationManager.ConnectionStrings["OdmConnection"].ConnectionString;

            using (SqlConnection connection = new SqlConnection(connString))
            {               
                string sql = "SELECT SiteID, SiteCode, SiteName FROM dbo.Sites";
                using (SqlCommand cmd = new SqlCommand(sql, connection))
                {
                    cmd.Connection.Open();

                    SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string code = reader.GetString(1);
                        GhcnSite site = new GhcnSite
                        {
                            SiteID = reader.GetInt32(0),
                            SiteCode = code,
                            SiteName = reader.GetString(2),  
                        };
                        lookup.Add(code, site);
                    }
                    reader.Close();
                    cmd.Connection.Close();
                }   
            }
            return lookup;
        }


        private Dictionary<string, GhcnVariable> getVariableLookup()
        {
            Dictionary<string, GhcnVariable> lookup = new Dictionary<string, GhcnVariable>();

            string connString = ConfigurationManager.ConnectionStrings["OdmConnection"].ConnectionString;

            using (SqlConnection connection = new SqlConnection(connString))
            {
                string sql = "SELECT VariableID, VariableCode, VariableName FROM dbo.Variables";
                using (SqlCommand cmd = new SqlCommand(sql, connection))
                {
                    cmd.Connection.Open();

                    SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string code = reader.GetString(1);
                        GhcnVariable variable = new GhcnVariable
                        {
                            VariableID = reader.GetInt32(0),
                            VariableCode = code,
                            VariableName = reader.GetString(2),
                        };
                        lookup.Add(code, variable);
                    }
                    reader.Close();
                    cmd.Connection.Close();
                }
            }
            return lookup;
        }

        public List<GhcnSeries> ReadSeriesFromInventory()
        {
            List<GhcnSeries> seriesList = new List<GhcnSeries>();
            Dictionary<string, TextFileColumn> colPos = new Dictionary<string,TextFileColumn>();
            colPos.Add("sitecode", new TextFileColumn(1, 11));
            colPos.Add("varcode", new TextFileColumn(32, 35));
            colPos.Add("firstyear", new TextFileColumn(37, 40));
            colPos.Add("lastyear", new TextFileColumn(42, 45));

            string url = "https://www1.ncdc.noaa.gov/pub/data/ghcn/daily/ghcnd-inventory.txt";

            var client = new WebClient();
            using (var stream = client.OpenRead(url))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string siteCode = line.Substring(colPos["sitecode"].Start, colPos["sitecode"].Length);
                    string varCode = line.Substring(colPos["varcode"].Start, colPos["varcode"].Length);
                    int firstYear = Convert.ToInt32(line.Substring(colPos["firstyear"].Start, colPos["firstyear"].Length));
                    int lastYear = Convert.ToInt32(line.Substring(colPos["lastyear"].Start, colPos["lastyear"].Length));

                    DateTime beginDateTime = new DateTime(firstYear, 1, 1);
                    DateTime endDateTime = new DateTime(lastYear, 12, 31);
                    if (lastYear == DateTime.Now.Year)
                    {
                        endDateTime = (new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)).AddDays(-1);
                    }
                    int valueCount = (int)((endDateTime - beginDateTime).TotalDays);

                    // only add series for the GHCN core variables (SNWD, PRCP, TMAX, TMIN, TAVG)
                    if (_variableLookup.ContainsKey(varCode) && _siteLookup.ContainsKey(siteCode))
                    {
                        seriesList.Add(new GhcnSeries
                        {
                            SiteCode = siteCode,
                            SiteID = _siteLookup[siteCode].SiteID,
                            SiteName = _siteLookup[siteCode].SiteName,
                            VariableCode = varCode,
                            VariableID = _variableLookup[varCode].VariableID,
                            BeginDateTime = beginDateTime,
                            EndDateTime = endDateTime,
                            ValueCount = valueCount
                        });
                    }
                }
            }
            return seriesList;
        }

        private void SaveSeries(GhcnSeries series, SqlConnection connection)
        {
            string sql = @"INSERT INTO dbo.SeriesCatalog(
                                SiteID, 
                                SiteCode, 
                                SiteName,
                                VariableID, 
                                VariableCode, 
                                BeginDateTime, 
                                EndDateTime,
                                ValueCount)
                            VALUES(
                                @SiteID, 
                                @SiteCode,
                                @SiteName, 
                                @VariableID, 
                                @VariableCode, 
                                @BeginDateTime, 
                                @EndDateTime, 
                                @ValueCount)";
            using (SqlCommand cmd = new SqlCommand(sql, connection))
            {
                connection.Open();
                cmd.Parameters.Add(new SqlParameter("@SiteID", series.SiteID));
                cmd.Parameters.Add(new SqlParameter("@SiteCode", series.SiteCode));
                cmd.Parameters.Add(new SqlParameter("@SiteName", series.SiteName));
                cmd.Parameters.Add(new SqlParameter("@VariableID", series.VariableID));
                cmd.Parameters.Add(new SqlParameter("@VariableCode", series.VariableCode));
                cmd.Parameters.Add(new SqlParameter("@BeginDateTime", series.BeginDateTime));
                cmd.Parameters.Add(new SqlParameter("@EndDateTime", series.EndDateTime));
                cmd.Parameters.Add(new SqlParameter("@ValueCount", series.ValueCount));
                cmd.ExecuteNonQuery();
                connection.Close();
            }
        }

        public void UpdateSeriesCatalog()
        {
            List<GhcnSeries> seriesList = ReadSeriesFromInventory();
            Console.WriteLine("updating series catalog for " + seriesList.Count.ToString() + " series ...");

            string connString = ConfigurationManager.ConnectionStrings["OdmConnection"].ConnectionString;
            using (SqlConnection connection = new SqlConnection(connString))
            {
                foreach (GhcnSeries series in seriesList)
                {
                    SaveSeries(series, connection);
                }
            }
        }

    }
}
