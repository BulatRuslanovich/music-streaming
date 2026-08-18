import {
  ChartColumn,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Clock,
  CornerDownRight,
  Disc3,
  Download,
  EllipsisVertical,
  Gauge,
  GripVertical,
  Heart,
  Image,
  Info,
  Library,
  ListMusic,
  ListVideo,
  LogOut,
  MicVocal,
  Moon,
  Music,
  Pause,
  Pencil,
  Play,
  Plus,
  Radio,
  Repeat,
  Repeat1,
  Search,
  Settings,
  Share2,
  Shield,
  Shuffle,
  SkipBack,
  SkipForward,
  Sun,
  Trash2,
  TriangleAlert,
  Upload,
  UserRound,
  Volume2,
  VolumeX,
  WifiOff,
  X,
  type LucideIcon,
} from "lucide-react";

export interface IconProps {
  size?: number;
  className?: string;
}

function outline(Icon: LucideIcon) {
  function Wrapped({ size = 20, className }: IconProps) {
    return (
      <Icon
        size={size}
        strokeWidth={1.8}
        className={className}
        aria-hidden="true"
        focusable="false"
      />
    );
  }

  Wrapped.displayName = Icon.displayName;
  return Wrapped;
}

function solid(Icon: LucideIcon) {
  function Wrapped({ size = 20, className }: IconProps) {
    return (
      <Icon
        size={size}
        strokeWidth={1.5}
        fill="currentColor"
        className={className}
        aria-hidden="true"
        focusable="false"
      />
    );
  }

  Wrapped.displayName = Icon.displayName;
  return Wrapped;
}

export const PlayIcon = solid(Play);
export const PauseIcon = solid(Pause);
export const PreviousIcon = solid(SkipBack);
export const NextIcon = solid(SkipForward);
export const MoreIcon = solid(EllipsisVertical);
export const GripIcon = solid(GripVertical);

export const ShuffleIcon = outline(Shuffle);
export const RepeatIcon = outline(Repeat);
export const RepeatOneIcon = outline(Repeat1);
export const VolumeIcon = outline(Volume2);
export const MuteIcon = outline(VolumeX);
export const QueueIcon = outline(ListVideo);
export const PlayNextIcon = outline(CornerDownRight);
export const RadioIcon = outline(Radio);
export const LyricsIcon = outline(MicVocal);
export const DataSaverIcon = outline(Gauge);

export const SearchIcon = outline(Search);
export const LibraryIcon = outline(Library);
export const AlbumIcon = outline(Disc3);
export const ArtistIcon = outline(UserRound);
export const NoteIcon = outline(Music);
export const PlaylistIcon = outline(ListMusic);
export const ChartIcon = outline(ChartColumn);
export const ClockIcon = outline(Clock);
export const SettingsIcon = outline(Settings);
export const ShieldIcon = outline(Shield);
export const OfflineIcon = outline(WifiOff);

export const ImageIcon = outline(Image);
export const UploadIcon = outline(Upload);
export const DownloadIcon = outline(Download);
export const PlusIcon = outline(Plus);
export const TrashIcon = outline(Trash2);
export const EditIcon = outline(Pencil);
export const CloseIcon = outline(X);
export const CheckIcon = outline(Check);
export const SignOutIcon = outline(LogOut);
export const SunIcon = outline(Sun);
export const MoonIcon = outline(Moon);
export const WarningIcon = outline(TriangleAlert);
export const ShareIcon = outline(Share2);
export const InfoIcon = outline(Info);

export const ChevronDownIcon = outline(ChevronDown);
export const ChevronUpIcon = outline(ChevronUp);
export const ChevronLeftIcon = outline(ChevronLeft);
export const ChevronRightIcon = outline(ChevronRight);

export function HeartIcon({
  size = 20,
  className,
  filled = false,
}: IconProps & { filled?: boolean }) {
  return (
    <Heart
      size={size}
      strokeWidth={1.8}
      fill={filled ? "currentColor" : "none"}
      className={className}
      aria-hidden="true"
      focusable="false"
    />
  );
}
