﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace MOE.Common.Models.Repositories
{
    public class ApproachCycleAggregationRepository : IApproachCycleAggregationRepository
    {
        private readonly SPM _db;

        public ApproachCycleAggregationRepository()
        {
            _db = new SPM();
        }

        public ApproachCycleAggregationRepository(SPM context)
        {
            _db = context;
        }

        public ApproachCycleAggregation Add(ApproachCycleAggregation approachCycleAggregation)
        {
            throw new NotImplementedException();
        }

        public int GetApproachCycleCountAggregationByApproachIdAndDateRange(int approachId, DateTime start,
            DateTime end)
        {
            var cycles = 0;
            if (_db.ApproachCycleAggregations.Any(r => r.ApproachId == approachId
                                                       && r.BinStartTime >= start && r.BinStartTime <= end))
                cycles = _db.ApproachCycleAggregations.Where(r => r.ApproachId == approachId
                                                                  && r.BinStartTime >= start &&
                                                                  r.BinStartTime <= end)
                    .Sum(r => r.TotalCycles);
            return cycles;
        }

        public void Remove(ApproachCycleAggregation approachCycleAggregation)
        {
            throw new NotImplementedException();
        }

        public List<ApproachCycleAggregation> GetApproachCyclesAggregationByApproachIdAndDateRange(int approachId,
            DateTime startDate, DateTime endDate, bool getProtectedPhase)
        {
            return _db.ApproachCycleAggregations.Where(r => r.ApproachId == approachId
                                                            && r.BinStartTime >= startDate &&
                                                            r.BinStartTime <= endDate
                                                            && r.IsProtectedPhase == getProtectedPhase).ToList();
        }


        public void Update(ApproachCycleAggregation approachCycleAggregation)
        {
            throw new NotImplementedException();
        }
    }
}