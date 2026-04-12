export interface ISeasonBangumi {
  id: string;
  mikanId: number;
  title: string;
  dayOfWeek: number;
  imageUrl: string | null;
  scrapedAt: string;
}

export interface ISeasonResponse {
  year: number | null;
  season: string | null;
  lastScrapedAt: string | null;
  bangumis: ISeasonBangumi[];
}

export interface IBangumiSubgroup {
  mikanSubgroupId: number;
  name: string;
  rssUrl: string;
}

export interface SeasonOption {
  year: number;
  season: string;
  label: string;
}
