using System.Text.Json.Serialization;

namespace TrainPlanner.Models;

// ── GET /api/v1/dictionaries/stations ──────────────────────────────────────────

public record PlkStationsResponse(
    [property: JsonPropertyName("stations")] List<PlkStationDto>? Stations,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalPages")] int TotalPages);

public record PlkStationDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

// ── GET /api/v1/schedules ─────────────────────────────────────────────────────

public record PlkScheduleResponse(
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt,
    [property: JsonPropertyName("period")] PlkDatePeriodDto? Period,
    [property: JsonPropertyName("routes")] List<PlkRouteDto>? Routes,
    [property: JsonPropertyName("dictionaries")] PlkDictionariesDto? Dictionaries);

public record PlkDatePeriodDto(
    [property: JsonPropertyName("from")] DateTime? From,
    [property: JsonPropertyName("to")] DateTime? To);

public record PlkRouteDto(
    [property: JsonPropertyName("scheduleId")] int ScheduleId,
    [property: JsonPropertyName("orderId")] int OrderId,
    [property: JsonPropertyName("trainOrderId")] int TrainOrderId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("carrierCode")] string? CarrierCode,
    [property: JsonPropertyName("nationalNumber")] string? NationalNumber,
    [property: JsonPropertyName("commercialCategorySymbol")] string? CommercialCategorySymbol,
    [property: JsonPropertyName("operatingDates")] List<DateOnly>? OperatingDates,
    [property: JsonPropertyName("stations")] List<PlkStationOnRouteDto>? Stations,
    [property: JsonPropertyName("connections")] List<PlkConnectionDto>? Connections);

public record PlkStationOnRouteDto(
    [property: JsonPropertyName("stationId")] int StationId,
    [property: JsonPropertyName("orderNumber")] int OrderNumber,
    [property: JsonPropertyName("arrivalTime")] string? ArrivalTime,
    [property: JsonPropertyName("arrivalDay")] int? ArrivalDay,
    [property: JsonPropertyName("departureTime")] string? DepartureTime,
    [property: JsonPropertyName("departureDay")] int? DepartureDay,
    [property: JsonPropertyName("arrivalPlatform")] string? ArrivalPlatform,
    [property: JsonPropertyName("departurePlatform")] string? DeparturePlatform,
    [property: JsonPropertyName("arrivalTrack")] string? ArrivalTrack,
    [property: JsonPropertyName("departureTrack")] string? DepartureTrack,
    [property: JsonPropertyName("arrivalTrainNumber")] string? ArrivalTrainNumber,
    [property: JsonPropertyName("departureTrainNumber")] string? DepartureTrainNumber,
    [property: JsonPropertyName("arrivalCommercialCategory")] string? ArrivalCommercialCategory,
    [property: JsonPropertyName("departureCommercialCategory")] string? DepartureCommercialCategory,
    [property: JsonPropertyName("stopTypeId")] int? StopTypeId,
    [property: JsonPropertyName("stopTypeName")] string? StopTypeName);

public record PlkConnectionDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("typeCode")] string? TypeCode,
    [property: JsonPropertyName("typeName")] string? TypeName,
    [property: JsonPropertyName("stationId")] int StationId,
    [property: JsonPropertyName("wagonNumbers")] string? WagonNumbers,
    [property: JsonPropertyName("train1OrderId")] int Train1OrderId,
    [property: JsonPropertyName("train1StationOrder")] int Train1StationOrder,
    [property: JsonPropertyName("train1DayOffset")] int Train1DayOffset,
    [property: JsonPropertyName("train2OrderId")] int Train2OrderId,
    [property: JsonPropertyName("train2StationOrder")] int Train2StationOrder,
    [property: JsonPropertyName("train2DayOffset")] int Train2DayOffset,
    [property: JsonPropertyName("operatingDates")] List<DateOnly>? OperatingDates);

public record PlkDictionariesDto(
    [property: JsonPropertyName("stations")] Dictionary<string, PlkStationDictionaryDto>? Stations,
    [property: JsonPropertyName("services")] Dictionary<string, PlkServiceDictionaryDto>? Services,
    [property: JsonPropertyName("connectionTypes")] Dictionary<string, string>? ConnectionTypes,
    [property: JsonPropertyName("carriers")] Dictionary<string, string>? Carriers,
    [property: JsonPropertyName("commercialCategories")] Dictionary<string, string>? CommercialCategories,
    [property: JsonPropertyName("stopTypes")] Dictionary<string, string>? StopTypes);

public record PlkStationDictionaryDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

public record PlkServiceDictionaryDto(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("description")] string? Description);

// ── GET /api/v1/operations ────────────────────────────────────────────────────

public record PlkOperationResponse(
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt,
    [property: JsonPropertyName("pagination")] PlkPaginationDto? Pagination,
    [property: JsonPropertyName("trains")] List<PlkTrainOperationDto>? Trains,
    [property: JsonPropertyName("stations")] Dictionary<string, string>? Stations);

public record PlkPaginationDto(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("totalPages")] int TotalPages,
    [property: JsonPropertyName("hasNextPage")] bool HasNextPage,
    [property: JsonPropertyName("hasPreviousPage")] bool HasPreviousPage);

public record PlkTrainOperationDto(
    [property: JsonPropertyName("scheduleId")] int ScheduleId,
    [property: JsonPropertyName("orderId")] int OrderId,
    [property: JsonPropertyName("trainOrderId")] int TrainOrderId,
    [property: JsonPropertyName("operatingDate")] DateOnly OperatingDate,
    [property: JsonPropertyName("trainStatus")] string? TrainStatus,
    [property: JsonPropertyName("stations")] List<PlkOperationStationDto>? Stations);

public record PlkOperationStationDto(
    [property: JsonPropertyName("stationId")] int StationId,
    [property: JsonPropertyName("plannedSequenceNumber")] int? PlannedSequenceNumber,
    [property: JsonPropertyName("actualSequenceNumber")] int ActualSequenceNumber,
    [property: JsonPropertyName("plannedDeparture")] DateTime? PlannedDeparture,
    [property: JsonPropertyName("plannedArrival")] DateTime? PlannedArrival,
    [property: JsonPropertyName("actualDeparture")] DateTime? ActualDeparture,
    [property: JsonPropertyName("actualArrival")] DateTime? ActualArrival,
    [property: JsonPropertyName("departureDelayMinutes")] int? DepartureDelayMinutes,
    [property: JsonPropertyName("arrivalDelayMinutes")] int? ArrivalDelayMinutes,
    [property: JsonPropertyName("isConfirmed")] bool IsConfirmed,
    [property: JsonPropertyName("isCancelled")] bool IsCancelled);

// ── GET /api/v1/dictionaries/carriers ──────────────────────────────────────────

public record PlkCarriersResponse(
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt,
    [property: JsonPropertyName("carriers")] List<PlkCarrierDto>? Carriers,
    [property: JsonPropertyName("usage")] PlkCarrierUsageInfo? Usage);

public record PlkCarrierDto(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("validFrom")] DateTime? ValidFrom,
    [property: JsonPropertyName("validTo")] DateTime? ValidTo);

public record PlkCarrierUsageInfo(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("examples")] List<string>? Examples);

// ── Error response ─────────────────────────────────────────────────────────────

public record PlkApiError(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("details")] object? Details,
    [property: JsonPropertyName("timestamp")] DateTime? Timestamp,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("traceId")] string? TraceId);
