﻿using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using MoreLinq;
using PCF_Functions;

using PCF_Model;

using PCF_Output;
using Shared;
using Shared.BuildingCoder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using pd = PCF_Functions.ParameterData;
using pdef = PCF_Functions.ParameterDefinition;
using plst = PCF_Functions.Parameters;

namespace PCF_Exporter
{
    public class PCFExport
    {
        internal Result ExecuteMyCommand(UIApplication uiApp, ref string msg)
        {
            // UIApplication uiApp = commandData.Application;
            //Test comment
            Document doc = uiApp.ActiveUIDocument.Document;
            UIDocument uidoc = uiApp.ActiveUIDocument;

            try
            {
                #region Declaration of variables
                // Instance a collector
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                //FilteredElementCollector pipeTypeCollector = new FilteredElementCollector(doc); //Obsolete???

                // Define a Filter instance to filter by System Abbreviation
                ElementParameterFilter sysAbbr = Shared.Filter.ParameterValueGenericFilter(doc, InputVars.SysAbbr, InputVars.SysAbbrParam);

                // Declare pipeline grouping object
                IEnumerable<IGrouping<string, Element>> pipelineGroupsOld;

                //Declare an object to hold collected elements from collector
                HashSet<Element> colElements = new HashSet<Element>();

                //Declare a collection for startpoints
                HashSet<IPcfElement> startPoints = new HashSet<IPcfElement>();

                //Collection to hold filtered elements
                HashSet<Element> elements = new HashSet<Element>();

                // Instance a collecting stringbuilder
                StringBuilder sbCollect = new StringBuilder();
                #endregion

                #region Compose preamble
                //Compose preamble
                Composer composer = new Composer();

                StringBuilder sbPreamble = composer.PreambleComposer();

                //Append preamble
                sbCollect.Append(sbPreamble);
                #endregion

                #region Element collectors
                //If user chooses to export a single pipeline get only elements in that pipeline and create grouping.
                //Grouping is necessary even tho theres only one group to be able to process by the same code as the all pipelines case

                //If user chooses to export all pipelines get all elements and create grouping
                if (InputVars.ExportAllOneFile)
                {
                    //Define a collector (Pipe OR FamInst) AND (Fitting OR Accessory OR Pipe).
                    //This is to eliminate FamilySymbols from collector which would throw an exception later on.
                    collector.WherePasses(new LogicalAndFilter(new List<ElementFilter>
                        {new LogicalOrFilter(new List<ElementFilter>
                        {
                            new ElementCategoryFilter(BuiltInCategory.OST_PipeFitting),
                            new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory),
                            new ElementClassFilter(typeof (Pipe))
                        }),
                            new LogicalOrFilter(new List<ElementFilter>
                            {
                                new ElementClassFilter(typeof(Pipe)),
                                new ElementClassFilter(typeof(FamilyInstance))
                            })
                        }));

                    colElements = collector.ToElements().ToHashSet();

                }

                else if (InputVars.ExportAllSepFiles || InputVars.ExportSpecificPipeLine)
                {
                    //Define a collector with multiple filters to collect PipeFittings OR PipeAccessories OR Pipes + filter by System Abbreviation
                    //System Abbreviation filter also filters FamilySymbols out.
                    collector.WherePasses(
                        new LogicalOrFilter(
                            new List<ElementFilter>
                            {
                                new ElementCategoryFilter(BuiltInCategory.OST_PipeFitting),
                                new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory),
                                new ElementClassFilter(typeof (Pipe))
                            })).WherePasses(sysAbbr);
                    colElements = collector.ToElements().ToHashSet();
                }

                else if (InputVars.ExportSelection)
                {
                    ICollection<ElementId> selection = uiApp.ActiveUIDocument.Selection.GetElementIds();
                    colElements = selection.Select(s => doc.GetElement(s)).ToHashSet();
                }
                #endregion

                #region Filtering
                try
                {
                    FilterOptions filterOptions = new FilterOptions()
                    {
                        FilterByDiameter = true,
                        FilterByPCF_ELEM_EXCL = true,
                        FilterByPCF_PIPL_EXCL = true,
                        FilterOutInstrumentPipes = true,
                        FilterOutSpecifiedPCF_ELEM_SPEC =
                            InputVars.PCF_ELEM_SPEC_FILTER.IsNullOrEmpty() == false,
                        FilterForIsogen = InputVars.ExportToIsogen
                    };

                    #region Old filtering
                    ////DiameterLimit filter applied to ALL elements.
                    //IEnumerable<Element> filtering = from element in colElements where Filters.FilterDL(element) select element;

                    ////Filter out EXCLUDED elements -> 0 means no checkmark
                    //filtering = from element in filtering
                    //            where element.get_Parameter(plst.PCF_ELEM_EXCL.Guid).AsInteger() == 0
                    //            select element;

                    ////Filter out EXCLUDED pipelines -> 0 means no checkmark
                    //filtering = filtering.Where(x => x.PipingSystemAllowed(doc) == true);

                    ////Remove instrument pipes
                    //filtering = filtering.ExceptWhere(x => x.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM)
                    //                                  .AsString() == "INSTR");

                    ////Filter out elements with specified PCF_ELEM_SPEC string
                    //if (InputVars.PCF_ELEM_SPEC_FILTER.IsNullOrEmpty() == false)
                    //{
                    //    filtering = filtering.ExceptWhere(x => x.get_Parameter(plst.PCF_ELEM_SPEC.Guid).AsString() == InputVars.PCF_ELEM_SPEC_FILTER);
                    //}

                    ////If exporting to ISO, remove some not needed elements
                    //if (InputVars.ExportToIsogen)
                    //{
                    //    //When exporting to Plant3D ISO creation, remove the group with the Piping System: Analysis Rigids (ARGD)
                    //    filtering = filtering
                    //        .Where(x => !(x.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM).AsString() == "ARGD"));

                    //    ////Also remove anchor symbols -> not needed for ISO
                    //    ////Currently not removed -> used for floor symbols
                    //    //filtering = filtering.ExceptWhere(x => x
                    //    //    .get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM)
                    //    //    .AsValueString() == "Support Symbolic: ANC");
                    //} 
                    #endregion

                    PCF_Filtering filter = new PCF_Filtering(colElements);
                    elements = filter.GetFilteredElements(doc, filterOptions);
                }
                catch (Exception ex)
                {
                    throw new Exception("Filtering in Main threw an exception:\n" + ex.Message +
                        "\nTo fix:\n" +
                        "1. See if parameter PCF_ELEM_EXCL exists, if not, rerun parameter import.");
                }
                #endregion

                //Create collection of OOP elements model
                var oopElements = elements.Select(
                    x => PcfElementFactory.CreatePhysicalElements(x))
                    .ToHashSet();

                var virtuals = oopElements.Select(
                    x => PcfElementFactory.CreateDependentVirtualElements(x))
                    .Aggregate((x, y) => x.Union(y));

                var specials = PcfElementFactory.CreateSpecialVirtualElements(oopElements);

                specials.RemoveWhere(e =>
                {
                    if (e is PCF_VIRTUAL_STARTPOINT sp)
                    {
                        startPoints.Add(sp);
                        return true;
                    }
                    return false;
                });

                oopElements.UnionWith(specials);
                oopElements.UnionWith(virtuals);

                #region Sub: Taps
                //Extract taps
                //So they do not mess up the material data
                var taps = oopElements.ExtractBy(x => x is PCF_TAP);
                #endregion

                #region Initialize Material Data
                //TEST: Do not write material data to elements with EXISTING-INCLUDE spec
                //HashSet<Element> existInclElements = elements.Where(x =>
                //    x.get_Parameter(plst.PCF_ELEM_SPEC.Guid).AsString() == "EXISTING-INCLUDE").ToHashSet();
                ////Remember the clearing of previous run data in transaction below

                //elements = elements.ExceptWhere(x =>
                //    x.get_Parameter(plst.PCF_ELEM_SPEC.Guid).AsString() == "EXISTING-INCLUDE").ToHashSet();

                //Set the start number to count the COMPID instances and MAT groups.
                int elementIdentificationNumber = 0;
                int materialGroupIdentifier = 0;

                //Make sure that every element has PCF_MAT_DESCR filled out.
                foreach (Element e in elements)
                {
                    if (string.IsNullOrEmpty(e.get_Parameter(plst.PCF_MAT_DESCR.Guid).AsString()))
                    {
                        uidoc.Selection.SetElementIds(new List<ElementId>(1) { e.Id });
                        BuildingCoderUtilities.ErrorMsg("PCF_MAT_DESCR is empty for element " + e.Id + "! Please, correct this issue before exporting again.");
                        throw new Exception("PCF_MAT_DESCR is empty for element " + e.Id + "! Please, correct this issue before exporting again.");
                    }
                }

                //Initialize material group numbers on the elements
                IEnumerable<IGrouping<string, IPcfElement>> materialGroups =
                    oopElements
                    .Where(x => x.ParticipateInMaterialTable) //<-- TAKE NOTICE!
                    .GroupBy(x => x.GetParameterValue(plst.PCF_MAT_DESCR));

                using (Transaction trans = new Transaction(doc, "Set PCF_ELEM_COMPID and PCF_MAT_ID"))
                {
                    trans.Start();
                    //Clear MTL data from previous runs for elements with EXISTING-INCLUDE spec
                    //foreach (Element e in existInclElements)
                    //{
                    //    e.get_Parameter(plst.PCF_ELEM_COMPID.Guid).Set("");
                    //    e.get_Parameter(plst.PCF_MAT_ID.Guid).Set("");
                    //}

                    //Access groups
                    foreach (var group in materialGroups)
                    {
                        materialGroupIdentifier++;
                        //Access parameters
                        foreach (var element in group)
                        {
                            elementIdentificationNumber++;
                            element.SetParameterValue(plst.PCF_ELEM_COMPID, elementIdentificationNumber.ToString());
                            element.SetParameterValue(plst.PCF_MAT_ID, materialGroupIdentifier.ToString());
                        }
                    }
                    trans.Commit();
                }

                #endregion

                //Using new OOP model for PCF elements from here
                //Wrap Revit elements to PCF elements
                var pipelineGroups = oopElements
                    .GroupBy(x => x.SystemAbbreviation)
                    .ToDictionary(x => x.Key, x => x.ToHashSet());

                using (TransactionGroup txGp = new TransactionGroup(doc))
                {
                    txGp.Start("Bogus transactionGroup for the break in hangers");
                    #region Pipeline management
                    foreach (KeyValuePair<string, HashSet<IPcfElement>> gp in pipelineGroups)
                    {
                        StringBuilder sbPipeline = new PCF_Pipeline.PCF_Pipeline_Export().Export(gp.Key, doc);
                        StringBuilder sbFilename = PCF_Pipeline.Filename.BuildAndWriteFilename(doc);
                        StringBuilder sbStartPoint = PCF_Pipeline.StartPoint.WriteStartPoint(gp.Key, startPoints);
                        StringBuilder sbEndsAndConnections = PCF_Pipeline.EndsAndConnections
                            .DetectAndWriteEndsAndConnections(gp.Key, gp, doc);

                        //BrokenPipes are NOT implemented YET
                        #region BrokenPipes

                        ////Here be code to handle break in accessories that act as supports
                        ////Find the supports in current acessoryList and add to supportList
                        ////Instantiate a brokenPipesGroup class

                        ////Collect all Connectors from brokenPipesList and find the longest distance
                        ////Create a temporary pipe from the Connectors with longest distance
                        ////Copy PCF_ELEM parameter values to the temporary pipe
                        ////Add the temporary pipe to the pipeList
                        ////Roll back the TransactionGroup after the elements are sent to Export class' Export methods.

                        //List<BrokenPipesGroup> bpgList = new List<BrokenPipesGroup>();

                        //List<Element> supportsList = accessoryList.Where(x => x.ComponentClass1(doc) == "Pipe Support").ToList();

                        //while (supportsList.Count > 0)
                        //{
                        //    //Get an element to start traversing
                        //    Element seedElement = supportsList.FirstOrDefault();
                        //    if (seedElement == null)
                        //        throw new Exception("BrokenPipes: Seed element returned null! supportsList.Count is " + supportsList.Count);

                        //    //Instantiate the BrokenPipesGroup
                        //    BrokenPipesGroup bpg = new BrokenPipesGroup(seedElement, gp.Key);

                        //    //Traverse system
                        //    bpg.Traverse(doc);

                        //    //Remove the support Elements from the collection to keep track of the while loop
                        //    foreach (Element support in bpg.SupportsOnPipe)
                        //    {
                        //        supportsList = supportsList.ExceptWhere(x => x.Id.IntegerValue == support.Id.IntegerValue).ToList();
                        //    }

                        //    bpgList.Add(bpg);
                        //}

                        //using (Transaction tx = new Transaction(doc))
                        //{
                        //    tx.Start("Create healed pipes");
                        //    foreach (BrokenPipesGroup bpg in bpgList)
                        //    {
                        //        //Remove the broken pipes from the pipeList
                        //        //If there's only one broken pipe, then there's no need to do anything
                        //        //If there's no broken pipes, then there's no need to do anything either
                        //        if (bpg.BrokenPipes.Count != 0 && bpg.BrokenPipes.Count != 1)
                        //        {
                        //            foreach (Element pipe in bpg.BrokenPipes)
                        //            {
                        //                pipeList = pipeList.ExceptWhere(x => x.Id.IntegerValue == pipe.Id.IntegerValue).ToHashSet();
                        //            }

                        //            //Using the new IEqualityComparer for Connectors to get distinct connectors in the collection
                        //            var brokenCons = MepUtils.GetALLConnectorsFromElements(bpg.BrokenPipes.ToHashSet(), new ConnectorXyzComparer(2.0.MmToFt()));
                        //            //Create distinct pair combinations with distance from all broken connectors
                        //            //https://stackoverflow.com/a/47003122/6073998
                        //            List<(Connector c1, Connector c2, double dist)> pairs = brokenCons
                        //                .SelectMany
                        //                    (
                        //                        (fst, i) => brokenCons.Skip(i + 1).Select(snd => (fst, snd, fst.Origin.DistanceTo(snd.Origin)))
                        //                    )
                        //                .ToList();
                        //            var longest = pairs.MaxBy(x => x.dist).FirstOrDefault();
                        //            Pipe dPipe = (Pipe)longest.c1.Owner;
                        //            bpg.HealedPipe = Pipe.Create(doc, dPipe.MEPSystem.GetTypeId(), dPipe.GetTypeId(),
                        //                dPipe.ReferenceLevel.Id, longest.c1.Origin, longest.c2.Origin);

                        //            Pipe donorPipe = (Pipe)bpg.BrokenPipes.FirstOrDefault();
                        //            bpg.HealedPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(donorPipe.Diameter);

                        //            //Add the healed pipe to the pipeList for processing
                        //            pipeList.Add(bpg.HealedPipe);
                        //        }
                        //    }
                        //    tx.Commit();
                        //}

                        ////Now the healed pipe must be populated by the parameters from a donorPipe
                        //using (Transaction tx = new Transaction(doc))
                        //{
                        //    //Gather all relevant parameter definitions
                        //    List<pdef> plist = plst.LPAll.Where(x => x.Domain == "ELEM" && x.Usage == "U").ToList();
                        //    plist.Add(plst.PCF_MAT_ID);

                        //    tx.Start("Populate the HealedPipe parameters!");
                        //    foreach (BrokenPipesGroup bpg in bpgList)
                        //    {
                        //        //Skip iteration if there's only 1 or no broken pipes
                        //        if (bpg.BrokenPipes.Count == 0 || bpg.BrokenPipes.Count == 1) continue;
                        //        Element donorPipe = bpg.BrokenPipes.FirstOrDefault();

                        //        foreach (pdef p in plist)
                        //        {
                        //            Parameter donorParameter = donorPipe.get_Parameter(p.Guid);
                        //            if (donorParameter == null) continue;
                        //            switch (donorParameter.StorageType)
                        //            {
                        //                case StorageType.None:
                        //                    continue;
                        //                case StorageType.Integer:
                        //                    int donorInt = donorParameter.AsInteger();
                        //                    if (donorInt == 0) continue;
                        //                    Parameter targetParInt = bpg.HealedPipe.get_Parameter(p.Guid);
                        //                    targetParInt.Set(donorInt);
                        //                    break;
                        //                case StorageType.Double:
                        //                    continue;
                        //                case StorageType.String:
                        //                    string donorStr = donorParameter.AsString();
                        //                    if (donorStr.IsNullOrEmpty()) continue;
                        //                    Parameter targetParStr = bpg.HealedPipe.get_Parameter(p.Guid);
                        //                    targetParStr.Set(donorStr);
                        //                    break;
                        //                case StorageType.ElementId:
                        //                    continue;
                        //                default:
                        //                    continue;
                        //            }
                        //        }
                        //    }
                        //    tx.Commit();
                        //}

                        #endregion

                        sbCollect.Append(sbPipeline); sbCollect.Append(sbFilename); 
                        sbCollect.Append(sbStartPoint); sbCollect.Append(sbEndsAndConnections);

                        #region Process TAPS
                        //Handle the new TAP-CONNECTION elements
                        //Write TAP-CONNECTION data to the affected elements
                        //ASSUMPTIONS:
                        //1. TAPS are always set on pipes -> cannot be connected to fittings/accessories
                        //this can be circumvented by using the old TAP method
                        //2. TAPS are always part of the same pipeline -> cannot be connected to other pipelines
                        using (Transaction tx = new Transaction(doc))
                        {
                            tx.Start("Process TAP-CONNECTION elements");
                            foreach (PCF_TAP tap in taps
                                .Where(x => x.SystemAbbreviation == gp.Key)) tap.ProcessTaps();
                            tx.Commit();
                        } 
                        #endregion

                        //Write the elements
                        sbCollect.Append(gp.Value.OrderBy(x => x.GetParameterValue(plst.PCF_ELEM_TYPE))
                            .Select(x => x.ToPCFString()).Aggregate((x, y) => x.Append(y)));
                    }
                    #endregion 

                    txGp.RollBack(); //RollBack the temporary created elements
                }

                #region Materials
                StringBuilder sbMaterials = composer.MaterialsSection(materialGroups);
                sbCollect.Append(sbMaterials);
                #endregion

                #region Output
                // Output the processed data
                PCF_Output.Output output = new PCF_Output.Output();
                output.OutputWriter(sbCollect);
                #endregion

            }

            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            catch (Exception ex)
            {
                throw new Exception(ex.ToString());
            }

            return Result.Succeeded;
        }
    }
}
